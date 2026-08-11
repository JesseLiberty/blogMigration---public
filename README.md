# Demo code associated with series of blog posts on https://jesseliberty.com

## Configuration

Secrets are read from the .NET [user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) store in
development and from environment variables in CI/production. There is no `config.json` file.

Set the required values once per machine:

```pwsh
dotnet user-secrets set "API_KEY" "<your-openai-api-key>"
dotnet user-secrets set "OPENAI_API_BASE" "https://aibe.mygreatlearning.com/openai/v1"
dotnet user-secrets set "TAVILY_API_KEY" "<your-tavily-api-key>"
```

Alternatively, provide the same keys as environment variables (`API_KEY`, `OPENAI_API_BASE`, `TAVILY_API_KEY`);
environment variables override user secrets.

Then run:

```pwsh
dotnet run
```

