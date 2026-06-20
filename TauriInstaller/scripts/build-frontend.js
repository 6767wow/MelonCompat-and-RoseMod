import fs from "node:fs";
import path from "node:path";

const root = path.resolve(import.meta.dirname, "..");
const source = path.join(root, "src");
const destination = path.join(root, "dist");

fs.rmSync(destination, { recursive: true, force: true });
fs.mkdirSync(destination, { recursive: true });

for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
  const from = path.join(source, entry.name);
  const to = path.join(destination, entry.name);
  if (entry.isDirectory()) {
    fs.cpSync(from, to, { recursive: true });
  } else {
    fs.copyFileSync(from, to);
  }
}
