using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Iced.Intel;

namespace RoseMod;

internal sealed class RoseModNativeDetour : IDisposable
{
    private const int Bits = 64;
    private const int AbsoluteJumpSize = 14;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;

    private readonly IntPtr original;
    private readonly IntPtr target;
    private readonly byte[] originalBytes;
    private readonly int patchSize;
    private bool applied;

    public RoseModNativeDetour(IntPtr original, IntPtr target)
    {
        if (original == IntPtr.Zero)
            throw new ArgumentNullException(nameof(original));
        if (target == IntPtr.Zero)
            throw new ArgumentNullException(nameof(target));

        this.original = original;
        this.target = target;

        var relocation = BuildRelocatedOriginal(original);
        patchSize = relocation.PatchSize;
        originalBytes = new byte[patchSize];
        Marshal.Copy(original, originalBytes, 0, originalBytes.Length);

        var trampolineSize = relocation.Code.Length + AbsoluteJumpSize;
        Trampoline = AllocateNear(original, (UIntPtr)trampolineSize);
        if (Trampoline == IntPtr.Zero)
            throw new InvalidOperationException("Could not allocate executable memory for RoseMod native trampoline.");

        var relocated = EncodeRelocatedInstructions(relocation.Instructions, Trampoline);
        WriteBytes(Trampoline, relocated);
        WriteBytes(IntPtr.Add(Trampoline, relocated.Length), CreateAbsoluteJump(IntPtr.Add(original, patchSize)));
    }

    public IntPtr Target => original;
    public IntPtr Detour => target;
    public IntPtr Trampoline { get; private set; }

    public void Apply()
    {
        if (applied)
            return;

        var patch = CreateAbsoluteJump(target);
        if (patchSize > patch.Length)
        {
            Array.Resize(ref patch, patchSize);
            for (var i = AbsoluteJumpSize; i < patch.Length; i++)
                patch[i] = 0x90;
        }

        WriteExecutable(original, patch);
        applied = true;
    }

    public Delegate GenerateTrampoline(Type delegateType)
    {
        if (!typeof(Delegate).IsAssignableFrom(delegateType))
            throw new ArgumentException("Type must be a delegate.", nameof(delegateType));
        if (Trampoline == IntPtr.Zero)
            throw new ObjectDisposedException(nameof(RoseModNativeDetour));

        return Marshal.GetDelegateForFunctionPointer(Trampoline, delegateType);
    }

    public void Dispose()
    {
        if (applied)
        {
            WriteExecutable(original, originalBytes);
            applied = false;
        }

        if (Trampoline != IntPtr.Zero)
        {
            VirtualFree(Trampoline, UIntPtr.Zero, MemRelease);
            Trampoline = IntPtr.Zero;
        }
    }

    private static RelocationPlan BuildRelocatedOriginal(IntPtr original)
    {
        var source = new byte[96];
        Marshal.Copy(original, source, 0, source.Length);

        var decoder = Decoder.Create(Bits, source, (ulong)original.ToInt64(), DecoderOptions.None);
        var instructions = new List<Instruction>();
        var copiedLength = 0;
        while (copiedLength < AbsoluteJumpSize)
        {
            var instruction = decoder.Decode();
            if (instruction.IsInvalid)
                throw new InvalidOperationException($"Could not decode instruction at 0x{instruction.IP:X} while building RoseMod native trampoline.");

            instructions.Add(instruction);
            copiedLength += instruction.Length;
        }

        var copied = new byte[copiedLength];
        Array.Copy(source, copied, copiedLength);
        return new RelocationPlan(instructions, copied, copiedLength);
    }

    private static byte[] EncodeRelocatedInstructions(IList<Instruction> instructions, IntPtr destination)
    {
        using var stream = new MemoryStream();
        var writer = new StreamCodeWriter(stream);
        var block = new InstructionBlock(writer, instructions, (ulong)destination.ToInt64());
        if (!BlockEncoder.TryEncode(Bits, block, out var error, out _, BlockEncoderOptions.None))
            throw new InvalidOperationException("Could not relocate native instructions for RoseMod trampoline: " + error);

        return stream.ToArray();
    }

    private static byte[] CreateAbsoluteJump(IntPtr destination)
    {
        var bytes = new byte[AbsoluteJumpSize];
        bytes[0] = 0xFF;
        bytes[1] = 0x25;
        Array.Copy(BitConverter.GetBytes((uint)0), 0, bytes, 2, 4);
        Array.Copy(BitConverter.GetBytes(destination.ToInt64()), 0, bytes, 6, 8);
        return bytes;
    }

    private static void WriteBytes(IntPtr destination, byte[] bytes)
    {
        Marshal.Copy(bytes, 0, destination, bytes.Length);
        FlushInstructionCache(GetCurrentProcess(), destination, (UIntPtr)bytes.Length);
    }

    private static void WriteExecutable(IntPtr destination, byte[] bytes)
    {
        if (!VirtualProtect(destination, (UIntPtr)bytes.Length, PageExecuteReadWrite, out var oldProtect))
            throw new InvalidOperationException($"VirtualProtect failed for 0x{destination.ToInt64():X}: {Marshal.GetLastWin32Error()}");

        try
        {
            WriteBytes(destination, bytes);
        }
        finally
        {
            VirtualProtect(destination, (UIntPtr)bytes.Length, oldProtect, out _);
        }
    }

    private static IntPtr AllocateNear(IntPtr origin, UIntPtr size)
    {
        var originAddress = origin.ToInt64();
        const long granularity = 0x10000;
        const long maxDistance = 0x7FFF0000;

        for (long distance = 0; distance <= maxDistance; distance += granularity)
        {
            foreach (var candidate in CandidateAddresses(originAddress, distance))
            {
                var allocated = VirtualAlloc(new IntPtr(candidate), size, MemCommit | MemReserve, PageExecuteReadWrite);
                if (allocated != IntPtr.Zero)
                    return allocated;
            }
        }

        return VirtualAlloc(IntPtr.Zero, size, MemCommit | MemReserve, PageExecuteReadWrite);
    }

    private static IEnumerable<long> CandidateAddresses(long originAddress, long distance)
    {
        var down = originAddress - distance;
        if (down > 0)
            yield return down & ~0xFFFFL;

        if (distance == 0)
            yield break;

        var up = originAddress + distance;
        if (up > 0)
            yield return up & ~0xFFFFL;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(IntPtr hProcess, IntPtr lpBaseAddress, UIntPtr dwSize);

    private sealed class RelocationPlan
    {
        public RelocationPlan(IList<Instruction> instructions, byte[] code, int patchSize)
        {
            Instructions = instructions;
            Code = code;
            PatchSize = patchSize;
        }

        public IList<Instruction> Instructions { get; }
        public byte[] Code { get; }
        public int PatchSize { get; }
    }
}
