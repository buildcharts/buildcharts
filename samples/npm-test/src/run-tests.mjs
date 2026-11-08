import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { spawnSync } from "node:child_process";

const results = process.env.BUILDCHARTS_RESULTS_DIR ?? "test-results";
mkdirSync(results, { recursive: true });
const result = spawnSync(process.execPath, [
  "node_modules/c8/bin/c8.js", "--reporter=cobertura", `--reports-dir=${results}`,
  "--include=dist/**", process.execPath, "--test", "--test-reporter=junit",
  `--test-reporter-destination=${join(results, "junit.xml")}`, "greeter.test.js",
], { stdio: "inherit" });
if (result.error) throw result.error;
process.exit(result.status ?? 1);
