using Jibo.Cloud.Application.Abstractions;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Application.Services;

public sealed partial class JiboInteractionService
{
    private async Task<JiboInteractionDecision> BuildWeatherReportDecisionAsync(
        TurnContext turn,
        string transcript,
        CancellationToken cancellationToken)
    {
        var referenceLocalTime = TryResolveReferenceLocalTime(turn);
        var catalog = await contentCache.GetCatalogAsync(cancellationToken);
        var normalizedTranscript = NormalizeCommandPhrase(transcript);
        var locationQuery = TryResolveWeatherLocationQuery(transcript);
        var weatherDate = ResolveWeatherDateEntity(turn, transcript, normalizedTranscript, referenceLocalTime);
        var isRangeForecastRequest = IsRangeForecastRequest(normalizedTranscript);
        var isOpenEndedForecastRequest = IsOpenEndedForecastRequest(
            normalizedTranscript,
            weatherDate,
            isRangeForecastRequest,
            locationQuery);
        if (ShouldDefaultForecastToTomorrow(
                normalizedTranscript,
                weatherDate,
                isRangeForecastRequest,
                isOpenEndedForecastRequest))
            weatherDate = new WeatherDateEntity("tomorrow", 1, "Tomorrow");

        if (weatherReportProvider is null)
            return new JiboInteractionDecision(
                "weather",
                ChooseWeatherServiceDownReply(catalog));

        var weatherCoordinates = string.IsNullOrWhiteSpace(locationQuery)
            ? TryResolveWeatherCoordinates(turn)
            : null;
        // Prefer the robot's known place name (e.g. Pleasant Hill) over OpenWeather's
        // nearest POI label (e.g. Lone Jack) when fetching by GPS.
        var preferredLocationName = string.IsNullOrWhiteSpace(locationQuery)
            ? TryResolveCurrentLocationName(turn)
            : null;
        var useCelsius = ShouldUseCelsius(turn, transcript);
        var isNextWeekForecast = IsNextWeekForecastRequest(normalizedTranscript, isRangeForecastRequest);
        var isThisWeekForecast = IsThisWeekForecastRequest(normalizedTranscript, isRangeForecastRequest);

        if (isNextWeekForecast || isThisWeekForecast || isOpenEndedForecastRequest)
        {
            const int rangeStartOffset = 1;
            var rangeEndOffset = isThisWeekForecast
                ? ResolveThisWeekForecastEndOffset(referenceLocalTime)
                : MaxWeatherForecastDayOffset;
            var weeklySnapshots = new List<(int DayOffset, WeatherReportSnapshot Snapshot)>();
            for (var offset = rangeStartOffset; offset <= rangeEndOffset; offset += 1)
            {
                WeatherReportSnapshot? weeklySnapshot;
                try
                {
                    weeklySnapshot = await weatherReportProvider.GetReportAsync(
                        new WeatherReportRequest(
                            locationQuery,
                            weatherCoordinates?.Latitude,
                            weatherCoordinates?.Longitude,
                            offset == 1,
                            useCelsius,
                            offset,
                            preferredLocationName),
                        cancellationToken);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    weeklySnapshot = null;
                }

                if (weeklySnapshot is not null)
                    weeklySnapshots.Add((
                        offset,
                        ApplyPreferredWeatherLocationName(weeklySnapshot, preferredLocationName, locationQuery)));
            }

            if (weeklySnapshots.Count == 0)
                return new JiboInteractionDecision(
                    "weather",
                    "I couldn't fetch the weather right now. Please try again.");

            var weeklySegments = BuildWeeklyForecastCardSegments(weeklySnapshots, referenceLocalTime);
            var weeklySpokenReply = BuildWeeklyForecastSpokenReply(
                weeklySegments,
                weeklySnapshots[0].Snapshot.LocationName,
                weeklySnapshots[0].Snapshot.UseCelsius,
                isThisWeekForecast);
            var weeklyWeatherPayload = BuildWeeklyWeatherSkillPayload(
                weeklySpokenReply,
                weeklySnapshots[0].Snapshot,
                weeklySegments,
                referenceLocalTime);
            AddWeatherRequestDiagnostics(
                weeklyWeatherPayload,
                transcript,
                normalizedTranscript,
                locationQuery,
                weatherDate,
                isRangeForecastRequest,
                isThisWeekForecast,
                isNextWeekForecast);
            return new JiboInteractionDecision(
                "weather",
                weeklySpokenReply,
                "chitchat-skill",
                weeklyWeatherPayload);
        }

        if (weatherDate.ForecastDayOffset > MaxWeatherForecastDayOffset)
            return new JiboInteractionDecision(
                "weather",
                $"I can forecast up to {MaxWeatherForecastDayOffset} days ahead. Try tomorrow or another day this week.");
        WeatherReportSnapshot? snapshot;
        try
        {
            snapshot = await weatherReportProvider.GetReportAsync(
                new WeatherReportRequest(
                    locationQuery,
                    weatherCoordinates?.Latitude,
                    weatherCoordinates?.Longitude,
                    string.Equals(weatherDate.DateEntity, "tomorrow", StringComparison.OrdinalIgnoreCase),
                    useCelsius,
                    weatherDate.ForecastDayOffset,
                    preferredLocationName),
                cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            snapshot = null;
        }

        if (snapshot is null)
            return new JiboInteractionDecision(
                "weather",
                ChooseWeatherServiceDownReply(catalog));

        snapshot = ApplyPreferredWeatherLocationName(snapshot, preferredLocationName, locationQuery);
        var spokenReply = BuildWeatherSpokenReply(snapshot, weatherDate, catalog);
        var weatherPayload = BuildWeatherSkillPayload(spokenReply, snapshot, referenceLocalTime);
        AddWeatherRequestDiagnostics(
            weatherPayload,
            transcript,
            normalizedTranscript,
            locationQuery,
            weatherDate,
            isRangeForecastRequest,
            isThisWeekForecast,
            isNextWeekForecast);
        return new JiboInteractionDecision(
            "weather",
            spokenReply,
            "chitchat-skill",
            weatherPayload);
    }

    private static WeatherReportSnapshot ApplyPreferredWeatherLocationName(
        WeatherReportSnapshot snapshot,
        string? preferredLocationName,
        string? locationQuery)
    {
        if (string.IsNullOrWhiteSpace(preferredLocationName) ||
            !string.IsNullOrWhiteSpace(locationQuery))
            return snapshot;

        return snapshot with { LocationName = preferredLocationName.Trim() };
    }

    private async Task<JiboInteractionDecision> BuildCommuteReportDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        var catalog = await contentCache.GetCatalogAsync(cancellationToken);

        if (commuteReportProvider is null)
            return new JiboInteractionDecision(
                "commute",
                ChooseCommuteServiceDownReply(catalog));

        CommuteReportSnapshot? snapshot;
        try
        {
            snapshot = await commuteReportProvider.GetReportAsync(turn, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            snapshot = null;
        }

        if (snapshot is null)
            return new JiboInteractionDecision(
                "commute",
                ChooseCommuteServiceDownReply(catalog));

        if (snapshot.RequiresSetup)
            return new JiboInteractionDecision(
                "commute_setup",
                ChooseCommuteAppSetupReply(catalog));

        var commuteReply = BuildCommuteSpokenReply(snapshot, catalog);
        var commutePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["skillId"] = "report-skill",
            ["cloudSkill"] = "commute",
            ["commute_view_enabled"] = true,
            ["commute_anim_cat"] = "commute",
            ["commute_anim_meta"] = ResolveCommuteAnimationMeta(snapshot),
            ["commute_duration_minutes"] = snapshot.DurationMinutes,
            ["commute_extra_minutes"] = snapshot.ExtraMinutes,
            ["commute_mode"] = snapshot.Mode,
            ["esml"] =
                $"<speak><anim cat='commute' meta='{ResolveCommuteAnimationMeta(snapshot)}' nonBlocking='true' /><break size='0.35'/><es cat='neutral' filter='!ssa-only, !sfx-only' endNeutral='true'>{EscapeForEsml(commuteReply)}</es></speak>"
        };
        return new JiboInteractionDecision(
            "commute",
            commuteReply,
            "report-skill",
            commutePayload);
    }

    private async Task<JiboInteractionDecision> BuildCalendarReportDecisionAsync(
        TurnContext turn,
        CancellationToken cancellationToken)
    {
        var catalog = await contentCache.GetCatalogAsync(cancellationToken);

        if (calendarReportProvider is null)
            return new JiboInteractionDecision(
                "calendar",
                ChooseCalendarServiceDownReply(catalog));

        CalendarReportSnapshot? snapshot;
        try
        {
            snapshot = await calendarReportProvider.GetReportAsync(turn, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            snapshot = null;
        }

        if (snapshot is null)
            return new JiboInteractionDecision(
                "calendar",
                ChooseCalendarServiceDownReply(catalog));

        return new JiboInteractionDecision(
            "calendar",
            BuildCalendarSpokenReply(snapshot, catalog));
    }

    private async Task<JiboInteractionDecision> BuildNewsDecisionAsync(
        TurnContext turn,
        string transcript,
        JiboExperienceCatalog catalog,
        CancellationToken cancellationToken,
        bool includeOutro = true)
    {
        var preferredCategories = ResolvePreferredNewsCategories(turn, transcript);
        if (newsBriefingProvider is not null)
            try
            {
                var snapshot = await newsBriefingProvider.GetBriefingAsync(
                    new NewsBriefingRequest(preferredCategories),
                    cancellationToken);

                if (snapshot?.Headlines.Count > 0)
                    return BuildProviderNewsDecision(
                        snapshot,
                        catalog,
                        preferredCategories,
                        MaxNewsHeadlines,
                        includeOutro);

                var providerStatus = ResolveNewsProviderStatus(snapshot);
                var providerMessage = snapshot?.ProviderMessage;
                var providerEndpoint = snapshot?.ProviderEndpoint;
                var providerHttpStatusCode = snapshot?.ProviderHttpStatusCode;
                var providerErrorCode = snapshot?.ProviderErrorCode;

                var fallbackBriefingWhenEmpty = randomizer.Choose(catalog.NewsBriefings);
                return BuildNewsDecision(
                    fallbackBriefingWhenEmpty,
                    null,
                    preferredCategories.Count > 0 ? preferredCategories : null,
                    null,
                    BuildNewsProviderDiagnostics(
                        providerStatus,
                        preferredCategories,
                        MaxNewsHeadlines,
                        snapshot?.Headlines.Count ?? 0,
                        providerMessage,
                        providerHttpStatusCode,
                        providerEndpoint,
                        providerErrorCode));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Provider failures should never block baseline news behavior.
                var fallbackBriefingOnError = randomizer.Choose(catalog.NewsBriefings);
                return BuildNewsDecision(
                    fallbackBriefingOnError,
                    null,
                    preferredCategories.Count > 0 ? preferredCategories : null,
                    null,
                    BuildNewsProviderDiagnostics(
                        "provider_exception",
                        preferredCategories,
                        MaxNewsHeadlines));
            }

        var fallbackBriefing = randomizer.Choose(catalog.NewsBriefings);
        return BuildNewsDecision(
            fallbackBriefing,
            null,
            preferredCategories.Count > 0 ? preferredCategories : null,
            null,
            BuildNewsProviderDiagnostics(
                "provider_unavailable",
                preferredCategories,
                MaxNewsHeadlines));
    }
}