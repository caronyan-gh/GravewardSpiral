using System.Text.Json;
using System.Text.Json.Nodes;
using GravewardSpiral.Core.Content;
using GravewardSpiral.Core.Events;

namespace GravewardSpiral.Tests.Events;

public partial class EventTests
{
    [Fact]
    public void ValueCasesShareSubjectButAllowDifferentComparedValues()
    {
        var s = new EventStep { Type = "cases", Cases = [new() { When = [new() { Type = "flag", Id = "a" }] }, new() { When = [new() { Type = "flag", Id = "b" }] }] };
        Assert.Null(EventBranches.CommonSubject(s));
        s.Cases[1].When[0].Id = "a";
        s.Cases[1].When[0].Not = true;
        Assert.NotNull(EventBranches.CommonSubject(s));
        s.Cases[0].When[0] = new() { Type = "cast", Id = "light" };
        s.Cases[1].When[0] = new() { Type = "cast", Id = "fireball" };
        Assert.NotNull(EventBranches.CommonSubject(s));
        s.Cases[0].When[0] = new() { Type = "chance", Value = 10 };
        s.Cases[1].When[0] = new() { Type = "chance", Value = 30 };
        Assert.Null(EventBranches.CommonSubject(s));
    }

    private static EventStep Flag(string id) => new() { Type = "flag", Id = id };
    private static EventStep ReadStep(JsonNode json) => json.Deserialize<EventStep>(ContentLibrary.JsonOptions)!;

    [Theory]
    [InlineData(0, "low")]
    [InlineData(10, "low")]
    [InlineData(11, "middle")]
    [InlineData(30, "middle")]
    [InlineData(31, "other")]
    public void CasesMatchIfChainsAtBoundariesAndAfterRoundTrip(int value, string expected)
    {
        var original = JsonNode.Parse("""
            {"type":"if","when":[{"type":"counter","id":"score","op":"<=","value":10}],
             "then":[{"type":"flag","id":"low"}],"else":[
              {"type":"if","when":[{"type":"counter","id":"score","op":"<=","value":30}],
               "then":[{"type":"flag","id":"middle"}],"else":[{"type":"flag","id":"other"}]}]}
            """)!.AsObject();
        var converted = EventBranches.ToCases(original);
        foreach (var json in new[] { original, converted, EventBranches.ToIf(converted) })
        {
            var (s, state) = Room("lever_room.txt", 1, 1);
            s.Counters["score"] = value;
            Drive(new EventRunner(new EventDef { Id = "case_test", Steps = [ReadStep(json), Flag("after")] }, Ctx(s, state)));
            Assert.Equal(new[] { expected }, s.Flags.Where(f => f is "low" or "middle" or "other"));
            Assert.Contains("after", s.Flags);
        }
        Assert.Equal("if", (string?)original["type"]); // Conversion does not mutate its input.
    }

    [Fact]
    public void CasesRequireEveryConditionAndDoNotFallThroughEvenWithEmptyThen()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var branch = new EventStep { Type = "cases", Cases =
        [
            new() { When = [new() { Type = "level", Value = 1 }, new() { Type = "flag", Id = "missing" }], Then = [Flag("wrong")] },
            new() { When = [], Then = [] },
            new() { When = [], Then = [Flag("wrong")] },
        ], Else = [Flag("wrong")] };
        Drive(new EventRunner(new EventDef { Id = "empty_then", Steps = [branch, Flag("after")] }, Ctx(s, state)));
        Assert.DoesNotContain("wrong", s.Flags);
        Assert.Contains("after", s.Flags);
        branch.Cases.Clear();
        branch.Else = [Flag("fallback")];
        Drive(new EventRunner(new EventDef { Id = "no_cases", Steps = [branch] }, Ctx(s, state)));
        Assert.Contains("fallback", s.Flags);
        var restored = ReadStep(EventBranches.ToIf(JsonSerializer.SerializeToNode(branch, ContentLibrary.JsonOptions)!.AsObject()));
        Assert.Empty(restored.When);
        Assert.Equal("fallback", Assert.Single(restored.Then).Id);
    }

    [Fact]
    public void CasesResumeAfterMessagesAndChoicesAndRespectAgain()
    {
        var (s, state) = Room("lever_room.txt", 1, 1);
        var e = Ev("""
            {"id":"case_flow","once":"game","steps":[
              {"type":"cases","cases":[{"when":[],"then":[
                {"type":"text","text":{"en":"First","ja":"最初"}},
                {"type":"choice","options":[{"label":{"en":"Leave","ja":"立ち去る"},"steps":[{"type":"again"}]},
                  {"label":{"en":"Stay","ja":"残る"},"steps":[{"type":"flag","id":"chosen"}]}]},
                {"type":"flag","id":"inside"}]}],"else":[{"type":"flag","id":"wrong"}]},
              {"type":"flag","id":"after"}]}
            """);
        var outputs = Drive(new EventRunner(e, Ctx(s, state)), [1]);
        Assert.Single(outputs.OfType<EventMessage>());
        Assert.Single(outputs.OfType<EventChoice>());
        Assert.Contains("chosen", s.Flags);
        Assert.Contains("inside", s.Flags);
        Assert.Contains("after", s.Flags);
        Assert.DoesNotContain("wrong", s.Flags);
        var (leave, leaveState) = Room("lever_room.txt", 1, 1);
        Drive(new EventRunner(e, Ctx(leave, leaveState)), [0]);
        Assert.DoesNotContain("inside", leave.Flags);
        Assert.DoesNotContain("after", leave.Flags);
    }

    [Fact]
    public void CasesPreserveRandomConditionEvaluationOrder()
    {
        var original = JsonNode.Parse("""
            {"type":"if","when":[{"type":"flag","id":"absent"},{"type":"chance","value":50}],
             "then":[{"type":"flag","id":"wrong"}],"else":[
              {"type":"if","when":[{"type":"chance","value":50}],"then":[{"type":"flag","id":"yes"}],
               "else":[{"type":"flag","id":"no"}]}]}
            """)!.AsObject();
        for (ulong seed = 1; seed <= 30; seed++)
        {
            var (a, aState) = Room("lever_room.txt", 1, 1);
            var (b, bState) = Room("lever_room.txt", 1, 1);
            var ca = Ctx(a, aState, seed); var cb = Ctx(b, bState, seed);
            Drive(new EventRunner(new EventDef { Id = "random_test", Steps = [ReadStep(original)] }, ca));
            Drive(new EventRunner(new EventDef { Id = "random_test", Steps = [ReadStep(EventBranches.ToCases(original))] }, cb));
            Assert.Equal(a.Flags.Order(), b.Flags.Order());
            Assert.Equal(ca.Rng.Next(0, 100000), cb.Rng.Next(0, 100000));
        }
    }

    [Fact]
    public void CasesValidateNestedConditionsStepsAndDraftPropertyNames()
    {
        var e = Ev("""
            {"id":"bad_cases","steps":[{"type":"cases","cases":[
              {"when":[{"type":"unknown_condition"},{"type":"item","id":"missing_item"}],
               "then":[{"type":"unknown_step"}]}],"else":[{"type":"run","id":"missing_event"}]}]}
            """);
        var problems = EventCheck.Check(Data, e, Content);
        foreach (string token in new[] { "unknown_condition", "missing_item", "unknown_step", "missing_event" })
            Assert.Contains(problems, p => p.Contains(token));
        Assert.Equal(2, EventDraftCheck.Conditions(e).Count());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EventDef>(
            """{"id":"typo","steps":[{"type":"cases","cases":[{"whne":[]}]}]}""", EventDraftCheck.Strict));
        var schema = FieldDocs.Schema(typeof(EventDef), FieldDocs.Load());
        Assert.Contains("cases", schema);
        Assert.Contains("EventCase", schema);
    }

    [Fact]
    public void ConversionKeepsElseSiblingsAndNestedBranchesAndIsIdempotent()
    {
        var events = JsonNode.Parse("""
            [{"id":"convert","steps":[{"type":"if","when":[],"then":[],"else":[
              {"type":"if","when":[],"then":[],"else":[{"type":"if","when":[],"then":[],"else":[]}]},
              {"type":"text","text":{"ja":"残す","en":"Keep"}}]}]}]
            """)!.AsArray();
        Assert.Equal(1, EventBranches.ConvertChains(events));
        var root = events[0]!["steps"]![0]!;
        Assert.Equal("if", (string?)root["type"]);
        Assert.Equal("cases", (string?)root["else"]![0]!["type"]);
        Assert.Equal("残す", (string?)root["else"]![1]!["text"]!["ja"]);
        string once = events.ToJsonString();
        Assert.Equal(0, EventBranches.ConvertChains(events));
        Assert.Equal(once, events.ToJsonString());
    }

    [Fact]
    public void RangeLabelsOnlySummarizeIncreasingBoundsOnTheSameSubject()
    {
        var s = new EventStep { Type = "cases", Cases =
        [new() { When = [new() { Type = "counter", Id = "score", Op = "<=", Value = 10 }] },
         new() { When = [new() { Type = "counter", Id = "score", Op = "<", Value = 31 }] }] };
        var ranges = EventBranches.Ranges(s)!;
        Assert.Equal(((long?)null, 10L), ranges[0]);
        Assert.Equal(((long?)11, 30L), ranges[1]);
        s.Cases[1].When[0].Id = "different";
        Assert.Null(EventBranches.Ranges(s));
        s.Cases[1].When[0].Id = "score";
        s.Cases[1].When[0].Value = 10;
        Assert.Null(EventBranches.Ranges(s));
        s.Cases[1].When[0].Value = 31;
        s.Cases[1].When.Add(new() { Type = "flag", Id = "extra" });
        Assert.Null(EventBranches.Ranges(s));
    }

    [Theory]
    [InlineData("srd")]
    [InlineData("scenarios/gravekeeper")]
    [InlineData("scenarios/sunken")]
    [InlineData("scenarios/devourer")]
    public void ContentBranchesKeepTheirConditionsAndPayloadsOnRoundTrip(string pack)
    {
        var events = JsonSerializer.Deserialize<List<EventDef>>(File.ReadAllText(Path.Combine(TestPaths.Content, pack, "events.json")), ContentLibrary.JsonOptions)!;
        IEnumerable<EventStep> Walk(IEnumerable<EventStep> steps) => steps.SelectMany(s => new[] { s }.Concat(s.Children().SelectMany(Walk)));
        foreach (var step in events.SelectMany(e => Walk(e.Steps)).Where(s => s.Type is "if" or "cases"))
        {
            var source = JsonSerializer.SerializeToNode(step, ContentLibrary.JsonOptions)!.AsObject();
            var converted = step.Type == "if"
                ? EventBranches.ToIf(EventBranches.ToCases(source))
                : EventBranches.ToCases(EventBranches.ToIf(source));
            Assert.Equal(JsonSerializer.Serialize(step, ContentLibrary.JsonOptions), JsonSerializer.Serialize(ReadStep(converted), ContentLibrary.JsonOptions));
        }
    }
}
