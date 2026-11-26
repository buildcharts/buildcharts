# java-test sample

This sample uses `java-build` to compile application and test sources and `java-test` to run JUnit tests with JaCoCo coverage.

## Run

```powershell
cd samples/java-test
./build.ps1
```

The script uses the BuildCharts tool from this checkout. Dependencies come from Maven Central. Surefire JUnit XML and `coverage.cobertura.xml` are exported to `.buildcharts/output/test`. The script fails if the JUnit reports are missing or contain failed tests.

## Azure Pipelines

Use [azure-pipelines.yml](azure-pipelines.yml) as the pipeline definition. The Bake step reports Maven compiler diagnostics with links to the source location. The publish tasks surface JUnit results in Tests and Cobertura in Code Coverage.
