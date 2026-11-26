package dev.buildcharts.samples;

import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.assertEquals;

class GreeterTest {
    private final Greeter greeter = new Greeter();

    @Test
    void greet_ShouldGreetByName() {
        var result = greeter.greet("Maven");

        assertEquals("Hello, Maven!", result);
    }

    @Test
    void greet_ShouldUseDefaultName() {
        var result = greeter.greet("");

        assertEquals("Hello, world!", result);
    }
}
