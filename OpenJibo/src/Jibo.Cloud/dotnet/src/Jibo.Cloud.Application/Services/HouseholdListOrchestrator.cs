using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

internal static class HouseholdListOrchestrator
{
    internal const string StateMetadataKey = "householdListState";
    internal const string TypeMetadataKey = "householdListType";
    internal const string DisplayTypeMetadataKey = "householdListDisplayType";
    internal const string NoMatchCountMetadataKey = "householdListNoMatchCount";
    internal const string NoInputCountMetadataKey = "householdListNoInputCount";

    private const string IdleState = "idle";
    private const string AwaitingItemState = "awaiting_item";
    private const string ShoppingListType = "shopping";
    private const string GroceryListType = "grocery";
    private const string TodoListType = "todo";

    private static readonly string[] ItemPrefixes =
    [
        "can you please add ",
        "can you add ",
        "could you please add ",
        "could you add ",
        "would you please add ",
        "would you add ",
        "add ",
        "put ",
        "buy ",
        "get ",
        "remind me to ",
        "i need to ",
        "i need ",
        "please add ",
        "please put "
    ];

    private static readonly string[] ItemSuffixes =
    [
        " to my shopping list",
        " to the shopping list",
        " on my shopping list",
        " in my shopping list",
        " for my shopping list",
        " to my grocery list",
        " to the grocery list",
        " on my grocery list",
        " in my grocery list",
        " for my grocery list",
        " my grocery list",
        " to my to do list",
        " to the to do list",
        " on my to do list",
        " in my to do list",
        " for my to do list",
        " to my todo list",
        " to the todo list",
        " on my todo list",
        " in my todo list",
        " for my todo list"
    ];

    public static Task<JiboInteractionDecision?> TryBuildDecisionAsync(
        TurnContext turn,
        string semanticIntent,
        string transcript,
        string loweredTranscript,
        IJiboRandomizer randomizer,
        IPersonalMemoryStore personalMemoryStore,
        Func<TurnContext, PersonalMemoryTenantScope> tenantScopeResolver)
    {
        var state = ReadString(turn, StateMetadataKey);
        var listType = ReadString(turn, TypeMetadataKey);
        var displayType = ReadString(turn, DisplayTypeMetadataKey);
        var isActiveState = !string.IsNullOrWhiteSpace(state) &&
                            !string.Equals(state, IdleState, StringComparison.OrdinalIgnoreCase);
        var isShoppingIntent = string.Equals(semanticIntent, "shopping_list", StringComparison.OrdinalIgnoreCase);
        var isTodoIntent = string.Equals(semanticIntent, "todo_list", StringComparison.OrdinalIgnoreCase);

        if (!isActiveState && !isShoppingIntent && !isTodoIntent)
            return Task.FromResult<JiboInteractionDecision?>(null);

        var resolvedListType = isShoppingIntent ? ShoppingListType :
            isTodoIntent ? TodoListType : NormalizeListType(listType);
        if (string.IsNullOrWhiteSpace(resolvedListType)) resolvedListType = ShoppingListType;
        var resolvedDisplayType = ResolveDisplayType(resolvedListType, displayType, isActiveState, loweredTranscript);

        var tenantScope = tenantScopeResolver(turn);

        if (ContainsAny(loweredTranscript, "cancel", "stop", "never mind", "nevermind", "forget it"))
            return Task.FromResult<JiboInteractionDecision?>(BuildCancelledDecision(resolvedListType,
                resolvedDisplayType));

        if (IsRecallRequest(loweredTranscript))
            return Task.FromResult<JiboInteractionDecision?>(BuildRecallDecision(
                resolvedListType,
                resolvedDisplayType,
                personalMemoryStore.GetListItems(tenantScope, resolvedListType)));

        var directItem = TryExtractListItem(loweredTranscript);
        if (string.IsNullOrWhiteSpace(directItem) && isActiveState)
        {
            if (IsConversationComplete(loweredTranscript))
                return Task.FromResult<JiboInteractionDecision?>(new JiboInteractionDecision(
                    BuildListIntentName(resolvedListType, "done"),
                    BuildDoneReply(resolvedDisplayType,
                        personalMemoryStore.GetListItems(tenantScope, resolvedListType)),
                    ContextUpdates: BuildContextUpdates(resolvedListType, resolvedDisplayType, IdleState)));

            directItem = NormalizeItem(transcript);
        }

        if (!string.IsNullOrWhiteSpace(directItem))
        {
            personalMemoryStore.AddListItem(tenantScope, resolvedListType, directItem);
            return Task.FromResult<JiboInteractionDecision?>(new JiboInteractionDecision(
                BuildListIntentName(resolvedListType, "add"),
                BuildAddedReply(resolvedDisplayType, directItem,
                    personalMemoryStore.GetListItems(tenantScope, resolvedListType)),
                ContextUpdates: BuildContextUpdates(resolvedListType, resolvedDisplayType, AwaitingItemState)));
        }

        if (string.IsNullOrWhiteSpace(transcript))
            return Task.FromResult<JiboInteractionDecision?>(new JiboInteractionDecision(
                BuildListIntentName(resolvedListType, "prompt"),
                BuildPromptReply(resolvedDisplayType),
                ContextUpdates: BuildContextUpdates(resolvedListType, resolvedDisplayType, AwaitingItemState)));

        return Task.FromResult<JiboInteractionDecision?>(new JiboInteractionDecision(
            BuildListIntentName(resolvedListType, "prompt"),
            BuildPromptReply(resolvedDisplayType),
            ContextUpdates: BuildContextUpdates(resolvedListType, resolvedDisplayType, AwaitingItemState)));
    }

    private static IDictionary<string, object?> BuildContextUpdates(string listType, string displayType, string state)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [StateMetadataKey] = state,
            [TypeMetadataKey] = listType,
            [DisplayTypeMetadataKey] = displayType,
            [NoMatchCountMetadataKey] = 0,
            [NoInputCountMetadataKey] = 0
        };
    }

    private static JiboInteractionDecision BuildCancelledDecision(string listType, string displayType)
    {
        return new JiboInteractionDecision(
            BuildListIntentName(listType, "cancel"),
            $"Okay. I stopped the {BuildListLabel(displayType)}.",
            ContextUpdates: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [StateMetadataKey] = IdleState,
                [TypeMetadataKey] = listType,
                [DisplayTypeMetadataKey] = displayType,
                [NoMatchCountMetadataKey] = 0,
                [NoInputCountMetadataKey] = 0
            });
    }

    private static JiboInteractionDecision BuildRecallDecision(string listType, string displayType,
        IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return new JiboInteractionDecision(
                BuildListIntentName(listType, "recall"),
                $"Your {BuildListLabel(displayType)} is empty.",
                ContextUpdates: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    [StateMetadataKey] = IdleState,
                    [TypeMetadataKey] = listType,
                    [DisplayTypeMetadataKey] = displayType,
                    [NoMatchCountMetadataKey] = 0,
                    [NoInputCountMetadataKey] = 0
                });

        return new JiboInteractionDecision(
            BuildListIntentName(listType, "recall"),
            $"Your {BuildListLabel(displayType)} has {JoinList(items)}.",
            ContextUpdates: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [StateMetadataKey] = IdleState,
                [TypeMetadataKey] = listType,
                [DisplayTypeMetadataKey] = displayType,
                [NoMatchCountMetadataKey] = 0,
                [NoInputCountMetadataKey] = 0
            });
    }

    private static string BuildAddedReply(string displayType, string addedItem, IReadOnlyList<string> items)
    {
        var itemLabel = BuildListLabel(displayType);
        return items.Count == 1
            ? $"Added {addedItem} to your {itemLabel}. What else should I add?"
            : $"Added {addedItem} to your {itemLabel}. You now have {JoinList(items)}.";
    }

    private static string BuildPromptReply(string displayType)
    {
        return $"What should I add to your {BuildListLabel(displayType)}?";
    }

    private static string BuildDoneReply(string displayType, IReadOnlyList<string> items)
    {
        return items.Count == 0
            ? $"Okay. Your {BuildListLabel(displayType)} is empty."
            : $"Okay. Your {BuildListLabel(displayType)} has {JoinList(items)}.";
    }

    private static string BuildListLabel(string displayType)
    {
        return NormalizeDisplayType(displayType) switch
        {
            GroceryListType => "grocery list",
            TodoListType => "to-do list",
            _ => "shopping list"
        };
    }

    private static string JoinList(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
        };
    }

    private static string? TryExtractListItem(string loweredTranscript)
    {
        foreach (var prefix in ItemPrefixes)
        {
            if (!loweredTranscript.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var remainder = loweredTranscript[prefix.Length..].Trim();
            if (IsListOnlyRemainder(remainder))
                return null;

            remainder = TrimTrailingListPhrases(remainder);
            return IsListOnlyRemainder(remainder) ? null : NormalizeItem(remainder);
        }

        return null;
    }

    private static bool IsRecallRequest(string loweredTranscript)
    {
        return ContainsAny(loweredTranscript,
            "what is on my shopping list",
            "what's on my shopping list",
            "show my shopping list",
            "what is on my grocery list",
            "what's on my grocery list",
            "show my grocery list",
            "what is on my to do list",
            "what's on my to do list",
            "show my to do list",
            "what are my tasks",
            "what do i need to buy",
            "what do i need to do");
    }

    private static string TrimTrailingListPhrases(string value)
    {
        var result = value;
        foreach (var suffix in ItemSuffixes)
            if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                result = result[..^suffix.Length].Trim();

        return result;
    }

    private static string NormalizeItem(string value)
    {
        return value.Trim().TrimEnd('.', ',', '!', '?');
    }

    private static string NormalizeListType(string? listType)
    {
        var normalized = NormalizeItem(listType ?? string.Empty).ToLowerInvariant();
        return normalized.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("to do", StringComparison.OrdinalIgnoreCase)
            ? TodoListType
            : normalized.Contains("shopping", StringComparison.OrdinalIgnoreCase) ||
              normalized.Contains("grocery", StringComparison.OrdinalIgnoreCase)
                ? ShoppingListType
                : string.Empty;
    }

    private static string ResolveDisplayType(string listType, string? storedDisplayType, bool isActiveState,
        string loweredTranscript)
    {
        var transcriptDisplayType = InferDisplayTypeFromTranscript(loweredTranscript);
        var normalizedStoredDisplayType = NormalizeDisplayType(storedDisplayType);

        if (isActiveState && !string.IsNullOrWhiteSpace(normalizedStoredDisplayType))
            return normalizedStoredDisplayType;

        if (!string.IsNullOrWhiteSpace(transcriptDisplayType))
            return transcriptDisplayType;

        if (!string.IsNullOrWhiteSpace(normalizedStoredDisplayType))
            return normalizedStoredDisplayType;

        return string.Equals(listType, TodoListType, StringComparison.OrdinalIgnoreCase)
            ? TodoListType
            : ShoppingListType;
    }

    private static string InferDisplayTypeFromTranscript(string loweredTranscript)
    {
        if (loweredTranscript.Contains("grocery", StringComparison.OrdinalIgnoreCase))
            return GroceryListType;

        if (loweredTranscript.Contains("to do", StringComparison.OrdinalIgnoreCase) ||
            loweredTranscript.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
            loweredTranscript.Contains("task", StringComparison.OrdinalIgnoreCase))
            return TodoListType;

        return loweredTranscript.Contains("shopping", StringComparison.OrdinalIgnoreCase)
            ? ShoppingListType
            : string.Empty;
    }

    private static string NormalizeDisplayType(string? displayType)
    {
        var normalized = NormalizeItem(displayType ?? string.Empty).ToLowerInvariant();
        return normalized.Contains("grocery", StringComparison.OrdinalIgnoreCase)
            ? GroceryListType
            : normalized.Contains("todo", StringComparison.OrdinalIgnoreCase) ||
              normalized.Contains("to do", StringComparison.OrdinalIgnoreCase)
                ? TodoListType
                : normalized.Contains("shopping", StringComparison.OrdinalIgnoreCase)
                    ? ShoppingListType
                    : string.Empty;
    }

    private static string BuildListIntentName(string listType, string action)
    {
        var normalizedListType = string.Equals(listType, TodoListType, StringComparison.OrdinalIgnoreCase)
            ? TodoListType
            : ShoppingListType;
        return $"{normalizedListType}_list_{action}";
    }

    private static bool IsListOnlyRemainder(string value)
    {
        var normalized = NormalizeItem(value).ToLowerInvariant();
        return normalized is "shopping list" or
            "grocery list" or
            "to do list" or
            "todo list" or
            "my shopping list" or
            "my grocery list" or
            "my to do list" or
            "my todo list" or
            "to my shopping list" or
            "to my grocery list" or
            "to my to do list" or
            "to my todo list" or
            "to the shopping list" or
            "to the grocery list" or
            "to the to do list" or
            "to the todo list" or
            "on my shopping list" or
            "on my grocery list" or
            "on my to do list" or
            "on my todo list" or
            "in my shopping list" or
            "in my grocery list" or
            "in my to do list" or
            "in my todo list" or
            "for my shopping list" or
            "for my grocery list" or
            "for my to do list" or
            "for my todo list";
    }

    private static bool ContainsAny(string loweredTranscript, params string[] phrases)
    {
        return phrases.Any(phrase => loweredTranscript.Contains(phrase, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConversationComplete(string loweredTranscript)
    {
        return ContainsAny(loweredTranscript,
            "done",
            "that's it",
            "that s it",
            "all set",
            "finished",
            "no more",
            "nothing else");
    }

    private static string? ReadString(TurnContext turn, string key)
    {
        return turn.Attributes.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
