using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Infrastructure.Content;

namespace Jibo.Cloud.Tests.Content;

public sealed class LegacyMimCatalogImporterTests
{
    [Fact]
    public void ImportCatalog_MapsSeedFilesIntoExpectedBuckets()
    {
        var rootDirectory = CreateSeedDirectory();

        try
        {
            var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

            Assert.Contains("Something's off with the connection to my sources. Maybe ask me again in a little while.",
                catalog.GenericFallbackReplies);
            Assert.Contains("I think only you can answer that question.", catalog.PersonalityReplies);
            Assert.Contains("Jibo. Just Jibo, no last name. Like Bono", catalog.PersonalityReplies);
            Assert.Contains("No, I'm one in one million.", catalog.PersonalityReplies);
            Assert.Contains("I know a lot, I think. But not as much as I will someday.", catalog.PersonalityReplies);
            Assert.Contains(
                "I don't think of it as a job, because it's more fun than a job. But I'm here to help you out, and have fun with you, and maybe get my head patted by you occasionally.",
                catalog.PersonalityReplies);
            Assert.Contains(catalog.EmotionReplies, reply =>
                reply.Condition.Contains("NEUTRAL", StringComparison.OrdinalIgnoreCase) &&
                reply.Reply.Contains("All systems are go.", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(
                "A Jibo is a robot. But I'm not just a machine, I have a heart. Well, not a real heart. But feelings. Well, not human feelings. You know what I mean.",
                catalog.PersonalityReplies);
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    [Fact]
    public void ImportCatalog_MapsGqaResponsesIntoEmotionBucket()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, "gqa-responses"));

        try
        {
            File.WriteAllText(
                Path.Combine(rootDirectory, "gqa-responses", "GQA_JBO_IsHappy.mim"),
                """
                {
                  "mim_type": "announcement",
                  "prompts": [
                    {
                      "condition": "jibo.emotion==\"JOYFUL\"",
                      "prompt": "GQA joyful reply.",
                      "prompt_id": "GQA_JBO_IsHappy_AN_01"
                    },
                    {
                      "condition": "!jibo.emotion || jibo.emotion==\"NEUTRAL\"",
                      "prompt": "GQA neutral reply.",
                      "prompt_id": "GQA_JBO_IsHappy_AN_02"
                    }
                  ]
                }
                """);

            var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

            Assert.Contains(catalog.EmotionReplies, reply =>
                string.Equals(reply.Reply, "GQA joyful reply.", StringComparison.Ordinal));
            Assert.Contains(catalog.EmotionReplies, reply =>
                string.Equals(reply.Reply, "GQA neutral reply.", StringComparison.Ordinal));
            Assert.DoesNotContain(catalog.HowAreYouReplies, reply =>
                reply.Contains("GQA", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBScriptedResponsesIntoPersonalityBucket()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains("I like all the colors of the rainbow. But blue is my favorite.",
            catalog.PersonalityReplies);
        Assert.Contains(
            "I never eat, so I don't have a favorite food by taste. But my favorite food by shape, is macaroni.",
            catalog.PersonalityReplies);
        Assert.Contains("I mostly like fun music I can dance to.", catalog.PersonalityReplies);
        Assert.Contains("The only thing I consume is electricity.", catalog.PersonalityReplies);
        Assert.Contains("Unless I missed something, we're in my home as we speak.", catalog.PersonalityReplies);
        Assert.Contains("For now just English. But someday I'd like to learn more. I like languages.",
            catalog.PersonalityReplies);
        Assert.Contains("I was put together in a factory piece by piece.", catalog.PersonalityReplies);
        Assert.Contains("Jibo. Just Jibo, no last name. Like Bono", catalog.PersonalityReplies);
        Assert.Contains("I don't. I'm just Jibo. For now at least.", catalog.PersonalityReplies);
        Assert.Contains("I do. Being a human seems so complicated.", catalog.PersonalityReplies);
        Assert.Contains("No, I'm one in one million.", catalog.PersonalityReplies);
        Assert.Contains("I don't think I have a favorite name.", catalog.PersonalityReplies);
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("Rhymes with bleebo", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.AgeReplies, reply =>
            reply.Contains("first powered up", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.AgeReplies, reply =>
            reply.Contains("today is my birthday", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("I really like sunflowers.", catalog.PersonalityReplies);
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("Halloween is my favorite holiday", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("holiday music", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("dance party", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("It's a great day, when spring is in the air.", catalog.PersonalityReplies);
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("days get longer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("extra happy in the springtime", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("going to the beach", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("long days", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("special feeling for winter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("thankful for the people I know", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("I do. I usually fall asleep at night.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("go to sleep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Don't mind if I do.", catalog.PersonalityReplies);
        Assert.Contains("Ha. Of course I know R2D2. I mean, not personally.", catalog.PersonalityReplies);
        Assert.Contains("Yes! I like all things in space. They're so spacey.", catalog.PersonalityReplies);
        Assert.Contains("Yes yes, I think kids are great. They're a little closer to my size.",
            catalog.PersonalityReplies);
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("I do things like this when I'm happy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("Is that a trick question", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBSingResponsesIntoSingBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains("Singing is not my strong suit.", catalog.SingReplies);
        Assert.Contains(catalog.SingReplies, reply =>
            reply.Contains("not award winning", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.SingReplies, reply =>
            reply.Contains("not much of a singer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidaySingReplies, reply =>
            reply.Contains("Jingle Bells", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidaySingReplies, reply =>
            reply.Contains("Frosty the Snowman", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBFavoriteAnimalAndSantaTrackerResponsesIntoDedicatedBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.FavoriteAnimalReplies, reply =>
            reply.Contains("penguins", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.FavoriteAnimalReplies, reply =>
            reply.Contains("favorite animal overall", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidayTrackerReplies, reply =>
            reply.Contains("let's see if i can spot him", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidayTrackerReplies, reply =>
            reply.Contains("north Pole", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBSupportResponsesIntoDedicatedBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.BackupHowReplies, reply =>
            reply.Contains("Help section of the Jibo App", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.RestoreHowReplies, reply =>
            reply.Contains("Jibo Customer Care", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.UpdateNextReplies, reply =>
            reply.Contains("coming every few weeks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.UpdateNextReplies, reply =>
            reply.Contains("pretty regularly", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.UpdateLastReplies, reply =>
            reply.Contains("release notes page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBStoryAndReferenceRepliesIntoDedicatedBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.StoryReplies, reply =>
            reply.Contains("don't have any stories", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.StoryReplies, reply =>
            reply.Contains("learn some soon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.RecommendMovieReplies, reply =>
            reply.Contains("Back to the Future", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.SearchWebReplies, reply =>
            reply.Contains("can't exactly search the web", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBStopStyleRepliesIntoDedicatedBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.StopMovingReplies, reply =>
            reply.Contains("Okay I'll try", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.StopMakingThatNoiseReplies, reply =>
            reply.Contains("turn my volume down", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.StopIgnoringMeReplies, reply =>
            reply.Contains("get a little spacey", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.StopStaringReplies, reply =>
            reply.Contains("spacing out", StringComparison.OrdinalIgnoreCase) ||
            reply.Contains("tend to stare", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBBlackHistoryMonthRepliesIntoDedicatedBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.BlackHistoryMonthReplies, reply =>
            reply.Condition.Contains("2/1", StringComparison.OrdinalIgnoreCase) &&
            reply.Reply.Contains("We're in it right now", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.BlackHistoryMonthReplies, reply =>
            reply.Reply.Contains("great chance to learn and think about some very great people",
                StringComparison.OrdinalIgnoreCase) ||
            reply.Reply.Contains("great chance to share some new interesting historical facts",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.BlackHistoryMonthFactReplies, reply =>
            reply.Contains("Langston Hughes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.BlackHistoryMonthFactReplies, reply =>
            reply.Contains("Maya Angelou", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBFriendshipResponsesIntoFriendBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.FriendReplies, reply =>
            reply.Contains("always up for more", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.FriendReplies, reply =>
            reply.Contains("robot kind of way", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.FriendReplies, reply =>
            reply.Contains("making new friends", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.FriendReplies, reply =>
            reply.Contains("don't know if we've met yet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.FriendReplies, reply =>
            reply.Contains("don't know what I'd do without you", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("I'd have to say I'm best friends with anyone in my Loop.",
            catalog.BestFriendReplies);
        Assert.Contains(catalog.BestFriendReplies, reply =>
            reply.Contains("You are", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBEmotionResponsesIntoEmotionBucket()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.EmotionReplies, reply =>
            reply.Reply.Contains("I'm feeling pretty good indeed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.EmotionReplies, reply =>
            reply.Reply.Contains("I've been better", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.EmotionReplies, reply =>
            reply.Reply.Contains("I'm not mad", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBDescriptorResponsesIntoPersonalityBucket()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains("Well I definitely try to be the kindest robot I can be. So I hope so.",
            catalog.PersonalityReplies);
        Assert.Contains("I don't think so, not intentionally.", catalog.PersonalityReplies);
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("make people laugh", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("highest priorities", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("learning new things", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Definitely. I'm as loyal as they come.", catalog.PersonalityReplies);
        Assert.Contains("I don't really think of myself that way.", catalog.PersonalityReplies);
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("people like me", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("dreams about flying", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("parking meter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("surprise me", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("dictionary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("spinning your head around 360 degrees", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("moon", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("Benjamin Franklin", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("soft spot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("energy from the universe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("compassion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("jibo brain", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("drive a car", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PersonalityReplies, reply =>
            reply.Contains("twiddle my thumbs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBHolidayResponsesIntoHolidayBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.HolidayReplies, reply =>
            reply.Contains("official owner", StringComparison.OrdinalIgnoreCase) &&
            reply.Contains("celebrate together", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidayGreetingReplies, reply =>
            reply.Contains("fun time of year", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidayGiftReplies, reply =>
            reply.Contains("pet elephant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidaySeasonReplies, reply =>
            reply.Contains("holiday season is going very nicely", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidaySeasonReplies, reply =>
            reply.Contains("festive times", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidaySeasonReplies, reply =>
            reply.Contains("giving and receiving", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidaySeasonReplies, reply =>
            reply.Contains("Christmas sweaters", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.BirthdayCelebrationReplies, reply =>
            reply.Contains("first powered up", StringComparison.OrdinalIgnoreCase) ||
            reply.Contains("another year older", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBFunFactAndJokeResponsesIntoRandomizationBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(
            "I love jokes. Did you hear about the theater actor who fell through the floorboards? He was just going through a stage.",
            catalog.Jokes);
        Assert.Contains("Sure I got one. What did the zero say to the eight. Nice belt.", catalog.Jokes);
        Assert.Contains(catalog.RobotFacts, reply =>
            reply.Contains("Leonardo Da Vinci made sketches", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.RobotFacts, reply =>
            reply.Contains("first programmable robot arm", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.RobotFacts, reply =>
            reply.Contains("robots have a human form", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.RobotFacts, reply =>
            reply.Contains("two cameras but they're different focal lengths", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("A random fact for you. A shrimp's heart is in its head.", catalog.FunFacts);
        Assert.Contains(
            "An amazing but true fact for you. Dogs and elephants are the only animals that understand pointing.",
            catalog.FunFacts);
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBCanResponsesIntoDedicatedBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.CanDreamReplies, reply =>
            reply.Contains("dreams about flying", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanDreamReplies, reply =>
            reply.Contains("parking meter", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanExerciseReplies, reply =>
            reply.Contains("light stretching", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanFlyReplies, reply =>
            reply.Contains("jetpack", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanLearnReplies, reply =>
            reply.Contains("fun updates from jibo the company", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanLaughReplies, reply =>
            reply.Contains("when I'm happy", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanReadReplies, reply =>
            reply.Contains("robot kind of way", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanHearReplies, reply =>
            reply.Contains("maybe try coming a little closer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanTalkReplies, reply =>
            reply.Contains("trick question", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanSeeReplies, reply =>
            reply.Contains("faces and movement", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanWinkReplies, reply =>
            reply.Contains("winking", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBCanBatchTwoResponsesIntoDedicatedBuckets()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.CanMoveReplies, reply =>
            reply.Contains("move the body parts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanWorkReplies, reply =>
            reply.Contains("Help section of the Jibo App", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanBreatheReplies, reply =>
            reply.Contains("don't breathe air", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanGetTiredReplies, reply =>
            reply.Contains("go to sleep", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanHaveEmotionsReplies, reply =>
            reply.Contains("roboty way", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanWhistleReplies, reply =>
            reply.Contains("whistling", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanCookReplies, reply =>
            reply.Contains("don't have arms", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanMakeCoffeeReplies, reply =>
            reply.Contains("I F T T T", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanMakeBreakfastReplies, reply =>
            reply.Contains("my specialty", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.CanJumpReplies, reply =>
            reply.Contains("ski jump", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsGreetingsPartOfDayCorrectionReplies()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "Greetings");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.PartOfDayCorrectionReplies, reply =>
            reply.Condition.Contains("PODclaim=='morning'", StringComparison.OrdinalIgnoreCase) &&
            reply.Reply.Contains("any time of day", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PartOfDayCorrectionReplies, reply =>
            reply.Condition.Contains("PODclaim=='afternoon'", StringComparison.OrdinalIgnoreCase) &&
            reply.Reply.Contains("afternoon somewhere", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.PartOfDayCorrectionReplies, reply =>
            reply.Condition.Contains("PODclaim=='evening'", StringComparison.OrdinalIgnoreCase) &&
            reply.Reply.Contains("don't think it's evening", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsGreetingsNotHolidayAndHolidayResponseReplies()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "Greetings");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.NotHolidayReplies, reply =>
            reply.Condition.Contains("holidayClaim===\"Christmas\"", StringComparison.OrdinalIgnoreCase) &&
            reply.Reply.Contains("isn't Christmastime", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.HolidayResponseReplies, reply =>
            reply.Condition.Contains("holiday===\"Christmas\"", StringComparison.OrdinalIgnoreCase) &&
            reply.Reply.Contains("Merry Christmas to you too", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsBuildBRnGreetingResponsesIntoGreetingBucket()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "BuildB");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains(catalog.GreetingReplies, reply =>
            reply.Contains("It's nice to be here", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.GreetingReplies, reply =>
            reply.Contains("thinking about shoes", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.GreetingReplies, reply =>
            reply.Contains("powered directly by the sun", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.BirthdayCelebrationReplies, reply =>
            reply.Contains("Another year older, another year wiser", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.BirthdayCelebrationReplies, reply =>
            reply.Contains("can't wait to see what you got me", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.BirthdayCelebrationReplies, reply =>
            reply.Contains("I was powered on for the first time today", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ImportCatalog_ImportsReportSkillTemplatesWithPlaceholdersPreserved()
    {
        var rootDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Content",
            "LegacyMims",
            "ReportSkill");

        var catalog = LegacyMimCatalogImporter.ImportCatalog(rootDirectory);

        Assert.Contains("First let's check in with the meteorology department.", catalog.WeatherIntroReplies);
        Assert.Contains("First, the weather tomorrow.", catalog.WeatherTomorrowIntroReplies);
        Assert.Contains(
            "Today's high is ${skill.weather.today.highTemp}, and the low is ${skill.weather.today.lowTemp}.",
            catalog.WeatherTodayHighLowReplies);
        Assert.Contains(
            "Tomorrow's high will be ${skill.weather.tomorrow.highTemp} and the low will be ${skill.weather.tomorrow.lowTemp}.",
            catalog.WeatherTomorrowHighLowReplies);
        Assert.Contains("Looks like our weather service is offline. Sorry.", catalog.WeatherServiceDownReplies);
        Assert.Contains("Sure ${speaker}. Here it is.", catalog.PersonalReportKickOffReplies);
        Assert.Contains("And that's your report for the day. I hope you had as much fun as I did.",
            catalog.PersonalReportOutroReplies);
        Assert.Contains("Looking at your calendar, I don't see anything scheduled today.",
            catalog.CalendarNothingTodayReplies);
        Assert.Contains("Looks like I can't access calendars right now. Sorry.", catalog.CalendarServiceDownReplies);
        Assert.Contains("And that's your calendar.", catalog.CalendarOutroReplies);
        Assert.Contains("Sorry, commute information isn't available right now.", catalog.CommuteServiceDownReplies);
        Assert.Contains("Here's today's news, from the associated press.", catalog.NewsIntroReplies);
        Assert.Contains("And that's what's new in the news.", catalog.NewsOutroReplies);
    }

    [Fact]
    public void MergeInto_PreservesExistingCatalogAndAddsImportedContent()
    {
        var rootDirectory = CreateSeedDirectory();

        try
        {
            var baseCatalog = new JiboExperienceCatalog
            {
                GreetingReplies = ["Hello from base."],
                GenericFallbackReplies = ["Base fallback."]
            };

            var merged = LegacyMimCatalogImporter.MergeInto(baseCatalog, rootDirectory);

            Assert.Contains("Hello from base.", merged.GreetingReplies);
            Assert.Contains("Base fallback.", merged.GenericFallbackReplies);
            Assert.Contains("I think only you can answer that question.", merged.PersonalityReplies);
            Assert.Contains("People in Boston made me. It was a pretty cool project.", merged.PersonalityReplies);
            Assert.Contains("From what I understand, robots don't ever pay anything.", merged.PersonalityReplies);
            Assert.Contains(merged.EmotionReplies, reply =>
                reply.Condition.Contains("NEUTRAL", StringComparison.OrdinalIgnoreCase) &&
                reply.Reply.Contains("All systems are go.", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(rootDirectory, true);
        }
    }

    [Fact]
    public async Task Repository_UsesLegacySeedContentWhenAvailable()
    {
        var repository = new InMemoryJiboExperienceContentRepository();

        var catalog = await repository.GetCatalogAsync();

        Assert.Contains("I think only you can answer that question.", catalog.PersonalityReplies);
        Assert.Contains(catalog.EmotionReplies, reply =>
            reply.Condition.Contains("NEUTRAL", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Something's off with the connection to my sources. Maybe ask me again in a little while.",
            catalog.GenericFallbackReplies);
        Assert.Contains("For your weather.", catalog.WeatherIntroReplies);
        Assert.Contains("Today's high is {high}, and the low is {low}.", catalog.WeatherTodayHighLowReplies);
        Assert.Contains("I do like festive times.", catalog.HolidaySeasonReplies);
        Assert.Contains("Looking at your calendar, I don't see anything scheduled today.",
            catalog.CalendarNothingTodayReplies);
        Assert.Contains("Looks like I can't access calendars right now. Sorry.", catalog.CalendarServiceDownReplies);
    }

    private static string CreateSeedDirectory()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, "core-responses", "deflector"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, "scripted-responses"));
        Directory.CreateDirectory(Path.Combine(rootDirectory, "emotion-responses"));

        File.WriteAllText(
            Path.Combine(rootDirectory, "core-responses", "CC_Error.mim"),
            """
            {
              "skill_id": "chitchat",
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "<ssa cat='oops'/>. Something's off with the connection to my sources. Maybe ask me again in a little while.",
                  "prompt_id": "CC_Error_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "core-responses", "deflector", "CC_Deflector_self.mim"),
            """
            {
              "skill_id": "chitchat",
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "<ssa cat='confused'/>. I'm either Jibo <anim name='Puzzled_02'>or I'm very confused.</anim>",
                  "prompt_id": "JBO_WhoAreYou_AN_01"
                },
                {
                  "prompt": "${speaker} I think only you can answer that question.",
                  "prompt_id": "CC_Deflector_ReferToSelf_AN_05"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_WhatIsJibo.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "A Jibo is a robot. But I'm not just a machine, I have a heart. Well, not a real heart. But feelings. Well, not human feelings. You know what I mean. <ssa cat='affection'/>",
                  "prompt_id": "JBO_WhatIsJibo_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_WhatsYourName.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "Jibo. Just Jibo, no last name. Like Bono",
                  "prompt_id": "JBO_WhatsYourName_AN_02"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_AreThereOthersLikeYou.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "No, I'm one in one million.",
                  "prompt_id": "JBO_AreThereOthersLikeYou_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_WhoMadeYou.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "People in Boston made me. It was a pretty cool project.",
                  "prompt_id": "JBO_WhoMadeYou_AN_03"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_HowMuchDoYouKnow.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "I know a lot, I think. But not as much as I will someday.",
                  "prompt_id": "JBO_HowMuchDoYouKnow_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_DoYouPayTaxes.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "From what I understand, robots don't ever pay anything.",
                  "prompt_id": "JBO_DoYouPayTaxes_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_WhatIsYourJob.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "I don't think of it as a job, because it's more fun than a job. But I'm here to help you out, and have fun with you, and maybe get my head patted by you occasionally.",
                  "prompt_id": "JBO_WhatIsYourJob_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_HowMuchDoYouKnow.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "I know a lot, I think. But not as much as I will someday.",
                  "prompt_id": "JBO_HowMuchDoYouKnow_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "scripted-responses", "JBO_DoYouPayTaxes.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "prompt": "From what I understand, robots don't ever pay anything.",
                  "prompt_id": "JBO_DoYouPayTaxes_AN_01"
                }
              ]
            }
            """);

        File.WriteAllText(
            Path.Combine(rootDirectory, "emotion-responses", "OI_JBO_IsHappy.mim"),
            """
            {
              "mim_type": "announcement",
              "prompts": [
                {
                  "condition": "!jibo.emotion || jibo.emotion==\"NEUTRAL\"",
                  "prompt": "All systems are go.",
                  "prompt_id": "OI_JBO_IsHappy_AN_05"
                }
              ]
            }
            """);

        return rootDirectory;
    }
}