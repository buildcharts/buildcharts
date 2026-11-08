import { cpSync, mkdirSync } from "node:fs";

const output = process.argv.find(value => value.startsWith("--output-path="))?.split("=")[1] ?? "dist";
mkdirSync(`${output}/browser`, { recursive: true });
for (const path of ["index.html", "app.js", "assets"]) {
  cpSync(path, `${output}/browser/${path}`, { recursive: true });
}
