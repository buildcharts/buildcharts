package dev.buildcharts.samples;

public final class Greeter {
    public String greet(String name) {
        if (name == null || name.isBlank()) {
            return "Hello, world!";
        }

        return "Hello, " + name + "!";
    }
}
