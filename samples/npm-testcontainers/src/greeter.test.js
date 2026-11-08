import assert from "node:assert/strict";
import { test } from "node:test";
import { GenericContainer, Wait } from "testcontainers";
import { getWelcomePage } from "./dist/greeter.js";

test("Nginx responds over HTTP", { timeout: 120000 }, async () => {
  const container = await new GenericContainer("nginx:alpine")
    .withExposedPorts(80)
    .withWaitStrategy(Wait.forHttp("/", 80).forStatusCode(200))
    .start();
  try {
    const result = await getWelcomePage(`http://${container.getHost()}:${container.getMappedPort(80)}/`);
    assert.match(result, /Welcome to nginx!/);
  } finally {
    await container.stop();
  }
});
