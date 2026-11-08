import assert from "node:assert/strict";
import { test } from "node:test";
import { greet } from "./dist/greeter.js";

test("greet returns a greeting", () => {
  assert.equal(greet("BuildCharts"), "Hello, BuildCharts!");
});

test("greet preserves the supplied name", () => {
  assert.equal(greet("npm"), "Hello, npm!");
});
