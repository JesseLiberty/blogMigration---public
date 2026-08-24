# Demo code associated with a [series of blog posts](https://jesseliberty.com)

This demonstration program, **Blog Writer**, is designed to research and write blog posts. It was written with *Microsoft Agent Framework* and the principal actors are the **BloggerAgent** which works as the orchestrator, the **ResearcherAgent** which goes out to the Web to research the requested topic, the **WriterAgent** which then writes the blog post, and the **ReviewerAgent** which reviews the proposed blog post, sending it back to the AuthorAgent if it is not approved.

The system prompts for each agent is contained in Prompts.cs

BlogWorkflow is responsible for creating the nodes and edges for moving through the workflow and also contains the logic for managing a breach of the token-cap (the maximum number of tokens that can be used in a single request, as defined in TokenCapChatClient).

## Miscellaneous Notes
* The program takes advantage of Tavily as a tool to search the Web. 
* All configuration is managed by Microsoft Secrets. 
* gpt-4o-mini is hard coded (for now) into the program.

## Additional Features
* Middleware is used to manage the tools. 
* OpenTelemetry is used to manage logging and emits a GenAI span per model round-trip
* ChatOptions sets the temperature to 0 for maximum consistency

## Known Issues
We are seeing a lot of calls to the LLM. Either there is a problem with the calls or with the telemetry.

## Next Steps
Primary next step is to demonstrate the deployment of the application to Foundry.
