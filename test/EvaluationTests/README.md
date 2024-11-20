## Running the Evaluation Tests

1. Make sure the eShopSupport application is configured and running on your local machine before running the tests. The tests will call into the eShopSupport APIs to collect AI responses, so this app must be up and running for evaluations to work.
1. Create a local folder alongside the eShopSupport repo to contain the cached AI responses.
1. Setup an [Azure OpenAI model deployment](https://azure.microsoft.com/en-us/products/ai-services/openai-service) to support the evaluation process. Take note of the model name, deployment name and endpoint during setup.
1. Edit the [appsettings.json](./appsettings.json) file in this folder to reflect the correct Azure OpenAI deployment settings and the path to the cache folder.
1. Run the tests from Visual Studio, VS Code, or `dotnet test`.

## Generating the Evaluation report

1. Update your dotnet tools by running
    ```cmd
    dotnet tool restore
    ```
1. Run the aieval report command to generate a report file.
    ```cmd
    dotnet aieval report --path /path/to/your/cache --output ./report.html
    ```
1. Open the `report.html` file in your web browser.