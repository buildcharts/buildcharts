import { copyFileSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";

const output = process.argv.find(value => value.startsWith("--output-path="))?.split("=")[1] ?? "dist";
const directory = `${output}/npm-npm`;
const { name, version, type } = JSON.parse(readFileSync("package.json", "utf8"));
mkdirSync(directory, { recursive: true });
copyFileSync("greeter.js", `${directory}/greeter.js`);
writeFileSync(`${directory}/package.json`, JSON.stringify({
  name, version, type, exports: "./greeter.js", files: ["greeter.js"],
}, null, 2) + "\n");
