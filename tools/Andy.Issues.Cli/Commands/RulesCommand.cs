using System.CommandLine;
using System.CommandLine.Invocation;
using Andy.Issues.Application.Dtos;
namespace Andy.Issues.Cli.Commands;

public static class RulesCommand
{
    private static ApiClient Client(InvocationContext ctx, Option<string> url, Option<string?> token) =>
        new(ctx.ParseResult.GetValueForOption(url)!, ctx.ParseResult.GetValueForOption(token));
    public static Command Build(Option<string> url, Option<string?> token)
    {
        var root = new Command("rules", "Manage named agent-rule profiles");
        var repo = new Argument<Guid>("repositoryId");
        var list = new Command("list", "List repository profiles") { repo };
        list.SetHandler(async (InvocationContext ctx) =>
        {
            using var api = Client(ctx, url, token);
            Console.WriteLine(ApiClient.ToJson(await api.GetAsync<AgentRuleProfileDto[]>($"api/repositories/{ctx.ParseResult.GetValueForArgument(repo)}/agent-rules/profiles")));
        });
        root.AddCommand(list);
        var rule = new Argument<Guid>("ruleId");
        var set = new Command("set-default", "Choose the repository default") { rule };
        set.SetHandler(async (InvocationContext ctx) =>
        {
            using var api = Client(ctx, url, token);
            await api.PostAsync($"api/agent-rules/{ctx.ParseResult.GetValueForArgument(rule)}/set-default");
        });
        root.AddCommand(set);
        var putRepo = new Argument<Guid>("repositoryId");
        var name = new Option<string>("--name") { IsRequired = true };
        var file = new Option<FileInfo>("--file") { IsRequired = true };
        var put = new Command("put", "Create or replace a profile by name from a Markdown file") { putRepo, name, file };
        put.SetHandler(async (InvocationContext ctx) =>
        {
            using var api = Client(ctx, url, token);
            var path = $"api/repositories/{ctx.ParseResult.GetValueForArgument(putRepo)}/agent-rules";
            var profileName = ctx.ParseResult.GetValueForOption(name)!.Trim();
            var body = await File.ReadAllTextAsync(ctx.ParseResult.GetValueForOption(file)!.FullName);
            var profiles = await api.GetAsync<AgentRuleProfileDto[]>(path + "/profiles") ?? [];
            var existing = profiles.FirstOrDefault(x => x.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
            var request = new AgentRuleWriteRequest(profileName, body, existing?.IsDefault ?? false, existing?.SortOrder ?? 0);
            if (existing is null) await api.PostAsync(path, request);
            else await api.PutAsync(path + $"/{existing.Id}", request);
        });
        root.AddCommand(put);
        return root;
    }
    public static Command BuildStories(Option<string> url, Option<string?> token)
    {
        var stories = new Command("stories", "Manage story rule selection");
        var rule = new Command("rule", "Choose a profile or inherit the default");
        var story = new Argument<Guid>("storyId");
        var id = new Argument<Guid?>("ruleId") { Arity = ArgumentArity.ZeroOrOne };
        var inherit = new Option<bool>("--default", "Inherit the repository default");
        var set = new Command("set", "Set the story's rule profile") { story, id, inherit };
        set.AddValidator(result =>
        {
            if (result.GetValueForOption(inherit) == result.GetValueForArgument(id).HasValue)
                result.ErrorMessage = "Provide either a ruleId or --default.";
        });
        set.SetHandler(async (InvocationContext ctx) =>
        {
            using var api = Client(ctx, url, token);
            await api.PutAsync($"api/stories/{ctx.ParseResult.GetValueForArgument(story)}/agent-rule", new StoryAgentRuleRequest(ctx.ParseResult.GetValueForArgument(id)));
        });
        rule.AddCommand(set); stories.AddCommand(rule); return stories;
    }
}
