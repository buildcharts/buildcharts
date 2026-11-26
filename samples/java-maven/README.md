# java-maven sample

This sample uses `java-build` to compile a Java library and `java-maven` to export its JAR and POM.

## Run

```powershell
cd samples/java-maven
./build.ps1
```

The script uses the BuildCharts tool from this checkout. The `MavenAuthenticate@v1` plugin passes the sample's `.mvn/settings.xml` to Maven as a BuildKit secret. The settings file configures a public Maven Central mirror and contains no credentials.

Find the JAR and POM under `.buildcharts/output/maven/`.
