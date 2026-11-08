import { copyFileSync, mkdirSync } from "node:fs";

const output = process.argv.find(value => value.startsWith("--output-path="))?.split("=")[1] ?? "dist";
mkdirSync(output, { recursive: true });
copyFileSync("greeter.js", `${output}/greeter.js`);
