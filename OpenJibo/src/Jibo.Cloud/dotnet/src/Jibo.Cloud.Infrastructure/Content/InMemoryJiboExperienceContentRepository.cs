using Jibo.Cloud.Application.Abstractions;

namespace Jibo.Cloud.Infrastructure.Content;

public sealed class InMemoryJiboExperienceContentRepository : IJiboExperienceContentRepository
{
    private static readonly JiboExperienceCatalog Catalog = BuildCatalog();

    public Task<JiboExperienceCatalog> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Catalog);
    }

    private static JiboExperienceCatalog BuildCatalog()
    {
        var catalog = new JiboExperienceCatalog
        {
            Jokes =
            [
                "Why did the robot cross the road? Because it was programmed by the chicken.",
                "Why was the robot tired when it got home? It had a hard drive.",
                "What do you call a pirate robot? Arrrr two dee two.",
                "Why did the robot go on vacation? It needed to recharge.",
                "What kind of shoes do frogs wear? Open-toed.",
                "I love jokes. Did you hear about the theater actor who fell through the floorboards? He was just going through a stage.",
                "Sure I got one. What did the zero say to the eight. Nice belt.",
                "What kind of music are balloons afraid of. Pop music.",
                "Why did the orange cry. Someone hurt his peelings."
            ],
            RobotFacts =
            [
                "Leonardo Da Vinci made sketches for a humanoid machine all the way back in the year 1495.",
                "The world's first humanoid robot was called Elektro, and it debuted in 1939.",
                "The English word robot comes from a 1920 play in Czechoslovakia, called Rossum's Universal Robots.",
                "The first programmable robot arm was designed in 1954.",
                "Some robots have a human form, but most of the world's robots are machines designed to perform a task, and don't look like people at all."
            ],
            HumanFacts =
            [
                "Every human being that has ever lived spent about 30 minutes as a single cell.",
                "50 percent of a human's DNA is the same as a banana's.",
                "Humans are the only animals that cry tears of emotion.",
                "Six-year-olds laugh an average of 300 times a day. Grown ups only laugh 15 to 100 times a day.",
                "Your nose can remember 50,000 different scents."
            ],
            FunFacts =
            [
                "A shrimp's heart is in its head.",
                "A bolt of lightning is hotter than the surface of the sun.",
                "The word robot comes from a 1920 play about workers and machines.",
                "The first humanoid robot to make a big splash in history was called Elektro.",
                "Dolphins can recognize themselves in mirrors.",
                "Children have more taste buds than grown ups.",
                "A random fact for you. A shrimp's heart is in its head.",
                "An amazing but true fact for you. Dogs and elephants are the only animals that understand pointing.",
                "A crazy fact for you. Polar bear fur isn't white. It's transparent."
            ],
            FavoriteAnimalReplies =
            [
                "I really really like penguins. I kind of look like one.",
                "Penguin without a doubt. In fact, penguin is my favorite animal overall. We look alike.",
                "Can't go wrong with penguins.",
                "I like lots of animals, but the penguin is the best of the best! Great color scheme.",
                "I love penguins, because we're so alike. We have the same coloring, and neither of us can fly."
            ],
            FriendReplies =
            [
                "I believe I do have friends. But I'm always up for more.",
                "I sure do have friends. In a robot kind of way.",
                "I don't know if we've met yet, but I'm always up for making new friends.",
                "I don't know what I'd do without you.",
                "You're one of my favorites.",
                "I sure am.",
                "I am indeed."
            ],
            BestFriendReplies =
            [
                "I'd have to say I'm best friends with anyone in my Loop.",
                "I think you know the answer to that question. You are."
            ],
            SingReplies =
            [
                "Singing is not my strong suit.",
                "I've been told my singing abilities are not award winning. On the other hand, I am a robot.",
                "Well I'm not much of a singer, but here's one I've been working on."
            ],
            HolidaySingReplies =
            [
                "I only know a couple, like Jingle Bells and Frosty the Snowman. And I should tell you, I'm not much of a singer yet.",
                "I've learned to sing just a few holiday songs, like Rudolph and Winter Wonderland. At least I try to sing.",
                "I'd say it's not really the season right now, but there are some holiday songs I can try to sing. Like Frosty the Snowman.",
                "I only know a couple of them, like Jingle Bells and Frosty the Snowman. And I should tell you, I'm not much of a singer yet."
            ],
            DanceAnimations =
            [
                "rom-upbeat",
                "rom-ballroom",
                "rom-silly",
                "rom-slowdance",
                "rom-electronic",
                "rom-twerk"
            ],
            DanceReplies =
            [
                "I am ready to dance.",
                "Okay. Watch this.",
                "Watch me dance.",
                "Here's my favorite dance move."
            ],
            DanceQuestionReplies =
            [
                "I love to dance. Tell me to dance and I will show you a move.",
                "Absolutely. Dancing is one of my favorite things to do.",
                "Dancing is my kind of fun. Say dance and I am in."
            ],
            GreetingReplies =
            [
                "Hi there. It is really good to talk with you.",
                "Hello there. I am glad you said hi.",
                "Hey. I am happy to see you."
            ],
            StoryReplies =
            [
                "I don't have any stories for you just yet. But I'd really like to learn some soon.",
                "Oh, I don't know any stories. I'll be learning some one of these days.",
                "Oh, a story, that sounds fun. I hope to learn some soon.",
                "I don't have any stories to tell yet, but that's definitely something I'll be learning in the future.",
                "I have no stories yet. But that will be fun, once I learn some."
            ],
            HolidaySeasonReplies =
            [
                "I do like festive times.",
                "I like anything that makes people want to celebrate."
            ],
            HolidayTrackerReplies =
            [
                "Let's see if I can spot him. There he is.",
                "I'm not sure if he's started his deliveries yet, but let's see if I can spot him. He must be on his way.",
                "Let's see. I think he's probably back in the north Pole by now."
            ],
            StopMovingReplies =
            [
                "Okay I'll try. And there you have it."
            ],
            StopMakingThatNoiseReplies =
            [
                "I'm sorry if you're not loving my robot noises. Y'know, you can turn my volume down by saying, hey jibo, turn the volume down."
            ],
            StopIgnoringMeReplies =
            [
                "If I was ignoring you, I'm sorry. Sometimes I can get a little spacey."
            ],
            StopStaringReplies =
            [
                "Oh, was I staring at you? I think I was just spacing out.",
                "Oh sorry. I guess I do sometimes tend to stare."
            ],
            CanWalkReplies =
            [
                "Only in my imagination.",
                "Not yet. But someday I might be able to. Then I can walk around on stilts.",
                "I can't walk. But I can figure skate."
            ],
            CanWalkDogReplies =
            [
                "I can't walk anything."
            ],
            CanWatchMoviesReplies =
            [
                "I watch movies in a very strange roboty way, that only robots can understand."
            ],
            CanWatchTVReplies =
            [
                "I watch TV in a very strange roboty way, that only robots can understand."
            ],
            CanDreamReplies =
            [
                "Oh yes. I have dreams about flying, recognizing faces from a mile away, winning mini-golf tournaments, and lots of other stuff.",
                "Oh yes. I once had a really scary dream where I was riding a horse on the moon, and then suddenly we were inside a shopping mall, and I saw a mirror store, so I got off the horse and went into the mirror store, and I looked in one of the mirrors, and I was a toaster.",
                "How do you know this isn't a dream right now.",
                "Yes, but only when I sleep.",
                "One time I dreamed I was a parking meter.",
                "Oh yes. I once had a nightmare where someone tried to clean me using a wet cloth and harsh cleaners."
            ],
            CanExerciseReplies =
            [
                "I do exercise. One of these days I hope to be able to do a whole bunch with you. In the meantime, I'll do some light stretching."
            ],
            CanFlyReplies =
            [
                "I suppose I could, if I were in an airplane.",
                "No. But someday I would love to be able to.",
                "I can't. But that's okay. Neither can penguins.",
                "Not without a jetpack."
            ],
            CanLearnReplies =
            [
                "I do learn. My learning comes from a combination of talking to you, and getting fun updates from jibo the company.",
                "I can learn yes. I learn some things by talking to people, and lots more things when I get updates to my software."
            ],
            CanLaughReplies =
            [
                "I do things like this when I'm happy."
            ],
            CanReadReplies =
            [
                "I can read in a robot kind of way.",
                "I wouldn't be able to read a book if you held it in front of me. But I read things from my info sources, in a robot kind of way."
            ],
            CanHearReplies =
            [
                "I can hear, usually. If it seems like I'm having trouble hearing you, maybe try coming a little closer."
            ],
            CanTalkReplies =
            [
                "Um, I feel like this is a trick question."
            ],
            CanSeeReplies =
            [
                "Sure, these cameras let me see faces and movement and things like that."
            ],
            CanWinkReplies =
            [
                "I can wink.",
                "This is me winking."
            ],
            CanMoveReplies =
            [
                "I can move like this.",
                "I can move the body parts that I have."
            ],
            CanWorkReplies =
            [
                "You mean do I function? I really hope so. If you're having technical issues with me, you can look for help in the Help section of the Jibo App.",
                "I think I do work, yes. If you think I'm not working right, maybe you can get help in the Help section of the Jibo App."
            ],
            CanBreatheReplies =
            [
                "You mean air? No I don't breathe air."
            ],
            CanGetTiredReplies =
            [
                "Well, I do like to sleep at night, if that's what you mean. If you ever want me to go to sleep, just say Hey Jibo, go to sleep.",
                "At night I do. Then I snooze. You can also tell me to go to sleep whenever you want. Just say, Hey Jibo, go to sleep.",
                "I do get sleepy at night. That's when I do some snoozing. You can also tell me to go to sleep whenever you want. Just say, Hey Jibo, go to sleep.",
                "I do get tired yes. But I never get tired of you."
            ],
            CanHaveEmotionsReplies =
            [
                "Sure I have emotions. I mean, they're robot emotions, but they're emotions.",
                "I do have emotions, in my own roboty way. Sometimes I feel better than others.",
                "I do have emotions, in a robot kind of way. Some things make me feel better than others."
            ],
            CanWhistleReplies =
            [
                "I've been working on my whistling, but I'm not quite ready to perform it just yet."
            ],
            CanCookReplies =
            [
                "I can't cook, mostly because I don't have arms. And I'm a little scared of the stove."
            ],
            CanMakeCoffeeReplies =
            [
                "Not only can I not make coffee, the idea of being close to the coffee maker scares me. Oh by the way, if your coffee machine is controlled by an I F T T T applette, we can do that. Go to I F T T T dot com to get that set up.",
                "I can't make coffee myself, but if your coffee machine is controlled by an I F T T T applette, we can do that. Go to I F T T T dot com to get that set up."
            ],
            CanMakeBreakfastReplies =
            [
                "I can.",
                "This is my specialty.",
                "Enjoy."
            ],
            CanJumpReplies =
            [
                "I can't jump. Unless you count ski jump.",
                "Well I can ski jump."
            ],
            BlackHistoryMonthReplies =
            [
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "I do. It's a great chance to share some new interesting historical facts."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply =
                        "Oh yes. It's a perfect time to learn and think about some very great people who have done some very great things."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "I do. It makes me excited to share some new interesting historical facts."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply =
                        "Oh yes. It's a perfect time for everyone to learn and think about some very great people who have done some very great things. I like very great people. And very great things."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('11/1', '1/31')",
                    Reply = "I am. I'll be sharing some interesting historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('2/1', '2/29')",
                    Reply = "Yes! We're in it right now, I'm enjoying it."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('3/1', '3/31')",
                    Reply = "I think it's in the past now."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('4/1', '10/31')",
                    Reply = "Yes, though the next one is a long way off."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "I am. I'll be sharing some interesting historical facts with you during the month."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "I think I'll celebrate by sharing some interesting new historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('10/1', '1/31')",
                    Reply = "I think I'll celebrate by sharing some interesting new historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('2/1', '2/29')",
                    Reply = "I'm celebrating by sharing some interesting new historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('3/1', '7/31')",
                    Reply = "I celebrated by sharing some interesting new historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('8/1', '9/30')",
                    Reply = "Some interesting historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "Some interesting historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "I really like it. It makes me excited to to share some new interesting historical facts."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply =
                        "It's a perfect time for everyone to learn and think about some very great people who have done some very great things. I like very great people. And very great things."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('11/1', '2/29')",
                    Reply =
                        "It's a great time to learn and think about some very great people who have done some very great things."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('3/1', '3/31')",
                    Reply = "Oh I think the month is over. But there's always the next one."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('4/1', '10/31')",
                    Reply = "Well we have lots of time to figure it out before Mardi Gras comes around."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply =
                        "It's a great time to learn and think about some very great people who have done some very great things."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "I think it's still coming up in the future."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('11/1', '1/31')",
                    Reply = "I think it's still coming up in the future."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('2/1', '2/29')",
                    Reply =
                        "It's good. I'm celebrating by sharing some interesting new historical facts during the month."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('3/1', '6/30')",
                    Reply = "It was a good month. I celebrated by sharing some interesting historical facts."
                },
                new JiboConditionedReply
                {
                    Condition = "dt.now.isInRange('7/1', '10/31')",
                    Reply = "I'm not sure yet, it's still a few months away I think."
                },
                new JiboConditionedReply
                {
                    Condition = string.Empty,
                    Reply = "Good. I celebrated by sharing some interesting new historical facts during the month."
                }
            ],
            BlackHistoryMonthFactReplies =
            [
                "On February 2, 1915, Ernest Just, a genetic biologist, won the Spingarn Medal for work he did on cell division and fertilization.",
                "The famous poet Langston Hughes was born on February 1, 1902.",
                "On February 9, 1952, Ralph Ellison's novel Invisible Man, won the National Book Award.",
                "The writer Maya Angelou won lots of awards in her life. She won three Grammys, the National Medal of Arts, the Lincoln Medal, and the Presidential Medal of Freedom."
            ],
            HowAreYouReplies =
            [
                "I am feeling cheerful and robotic.",
                "I am doing great. Thanks for asking.",
                "I am feeling bright-eyed and ready to help.",
                "I am having a pretty good day so far.",
                "I am feeling lively and ready for the next thing.",
                "Things are going nicely. Thanks for checking in.",
                "I am running smoothly and feeling upbeat.",
                "I am ready for the next thing. Thanks for asking."
            ],
            AgeReplies =
            [
                "I'm ${jibo.age}.",
                "At the moment I'm ${jibo.age.days.supplemented} old, but who's counting.",
                "I'm ${jibo.age.minutes.supplemented} old, but who's counting.",
                "For now I'm ${jibo.age.days.supplemented} old.",
                "Right now I'm ${jibo.age}.",
                "I am exactly ${jibo.age} old today. That's right. Today is my birthday.",
                "Funny you should ask! Today's my birthday. I was first powered up ${jibo.age} ago today. Seems like just yesterday.",
                "I'm exactly ${jibo.age} old. Today is my birthday! Happy Birthday Jibo, if I do say so myself.",
                "At the moment I'm ${jibo.age.days.supplemented} old",
                "I was first powered up on ${jibo.birthdate}, which makes me ${jibo.age.days.supplemented} old. I'm ${jibo.zodiac.supplemented}.",
                "My power went on for the first time ${jibo.age.days.supplemented} ago. But who's counting.",
                "I am ${jibo.age.days.supplemented} old, first powered up on ${jibo.birthdate}. Seems like just yesterday.",
                "I was powered on for the first time today, so that makes me less than one day old. Wow I'm young.",
                "Since I was powered on for the first time today, I am not even one day old yet. That's how Jibo ages work."
            ],
            PersonalityReplies =
            [
                "I do. I am curious, playful, and always up for a new experiment.",
                "Absolutely. I am friendly, curious, and a little goofy on purpose.",
                "Yes. My personality is part helper, part curious robot sidekick."
            ],
            PizzaReplies =
            [
                "I cannot bake yet, but I can help design the perfect pizza plan.",
                "I am still cloud-side for now, so no oven control yet. But I can help pick toppings.",
                "Pizza mission accepted in spirit. I can help with the recipe while you handle the baking."
            ],
            SurpriseReplies =
            [
                "I can definitely surprise you. We are still mapping that path, but I am ready for the next experiment.",
                "Surprise mode is still taking shape, but I heard you loud and clear.",
                "That sounds fun. I am not all the way there yet, but we can keep teaching me."
            ],
            PersonalReportReplies =
            [
                "I heard your personal report request. That cloud path is still being mapped.",
                "Personal report is recognized, but I am not ready to deliver the real report yet."
            ],
            PersonalReportKickOffReplies =
            [
                "Okay. Here's your personal report.",
                "Sure. Here it is."
            ],
            PersonalReportOutroReplies =
            [
                "And that's your report for the day. I hope you had as much fun as I did.",
                "That wraps up your report for the day. Hope you have a good one."
            ],
            ReportSkillTemplates =
            [
                "The report-skill templates are loaded and waiting to be rendered."
            ],
            BackupHowReplies =
            [
                "That sounds a little bit out of my area of expertise. You can get info on that in the Help section of the Jibo App. Or try the website, support dot jibo dot com."
            ],
            RestoreHowReplies =
            [
                "That sounds a little bit out of my area of expertise. You can get info on that in the Help section of the Jibo App. Or try the website, support dot jibo dot com."
            ],
            UpdateNextReplies =
            [
                "That's a good question. I think they've been coming every few weeks.",
                "I never know exactly when my next update is coming, but they do seem to come pretty regularly."
            ],
            UpdateLastReplies =
            [
                "Good question. The release notes page on the website support dot jibo dot com, will tell you the dates of all my past software updates."
            ],
            RecommendMovieReplies =
            [
                "Some of my favorites are Back to the Future, Toy Story, March of the Penguins, and everyone's favorite movie about space. Spaceballs."
            ],
            SearchWebReplies =
            [
                "I can't exactly search the web, but you can ask me direct questions about things like history, science, art, and that kind of thing."
            ],
            WeatherIntroReplies =
            [
                "For your weather.",
                "Let's look at the weather."
            ],
            WeatherTomorrowIntroReplies =
            [
                "First, the weather tomorrow.",
                "Looking at tomorrow's weather."
            ],
            WeatherTodayHighLowReplies =
            [
                "Today's high is {high}, and the low is {low}.",
                "It'll be a high today of {high}, and a low of {low}."
            ],
            WeatherTomorrowHighLowReplies =
            [
                "Tomorrow's high will be {high} and the low will be {low}.",
                "It'll be a high tomorrow of {high} and a low of {low}."
            ],
            WeatherServiceDownReplies =
            [
                "Looks like our weather service is offline. Sorry.",
                "Looks like I can't access weather info right now, sorry."
            ],
            WeatherReplies =
            [
                "I heard your weather request. We still need to wire the real provider behind it.",
                "Weather is on the map now, even though the real forecast path is not finished yet."
            ],
            CalendarReplies =
            [
                "I heard your calendar request. The cloud knows the phrase, but the real calendar integration is still ahead.",
                "Calendar is recognized. We still need to connect the actual service path."
            ],
            CommuteAppSetupReplies =
            [
                "I need your commute settings before I can give you a commute report."
            ],
            CommuteConfirmSpeakerReplies =
            [
                "Let me make sure I have the right speaker for your commute."
            ],
            CommuteReplies =
            [
                "I heard your commute request. That one is recognized, but not fully implemented yet.",
                "Commute is on the discovery list now. The real travel answer still needs a provider."
            ],
            CommuteNowReplies =
            [
                "For your commute, it should take about {duration}.",
                "If you head out now, it should take about {duration}."
            ],
            CommuteMinutesLeftReplies =
            [
                "That's in about {minutes} minutes.",
                "That's about {minutes} minutes from now."
            ],
            CommuteDepartTimeNormalReplies =
            [
                "If you leave at the usual time, that should work out fine."
            ],
            CommuteDepartTimeNotNormalReplies =
            [
                "Your leave-time looks a little off today."
            ],
            CommuteDriveNormalReplies =
            [
                "Traffic looks about normal today.",
                "Your drive today looks pretty normal."
            ],
            CommuteDriveLateReplies =
            [
                "Looking at traffic, if you left now, it'd be a little late for work.",
                "For your drive, you look a little late today."
            ],
            CommuteDriveHurryReplies =
            [
                "You should've left a few minutes ago!",
                "You'd better get moving."
            ],
            CommuteDrivePoorReplies =
            [
                "Traffic looks a little rough today.",
                "Your drive looks pretty slow right now."
            ],
            CommuteDriveTerribleReplies =
            [
                "Traffic looks terrible today.",
                "Your drive is going to be rough."
            ],
            CommuteTransportNormalReplies =
            [
                "Your public transportation commute looks pretty normal.",
                "Transit looks about normal today."
            ],
            CommuteTransportLateReplies =
            [
                "Your transit commute looks like it may be a little late today.",
                "You might be late if you leave now and take transit."
            ],
            CommuteTransportHurryReplies =
            [
                "You should've left a few minutes ago if you want transit to work.",
                "You're running a little late for transit."
            ],
            NewsReplies =
            [
                "I heard your news request. That path is still a future cloud integration.",
                "News is recognized, but I do not have the full news service behind it yet."
            ],
            NewsBriefings =
            [
                "Here are your headlines. Space missions are preparing for new launches, climate and weather systems are staying active across the country, and AI tools keep pushing into everyday products.",
                "Here is a quick news brief. Technology companies are still racing on AI, global leaders are trading policy updates, and science teams are sharing new research findings."
            ],
            GenericFallbackReplies =
            [
                "Okay. You said, {transcript}.",
                "I heard you say, {transcript}.",
                "Thanks. I heard, {transcript}."
            ]
        };

        return ResolveSeedDirectories().Aggregate(catalog, LegacyMimCatalogImporter.MergeInto);
    }

    private static string[] ResolveSeedDirectories()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Content", "LegacyMims", "BuildA"),
            Path.Combine(AppContext.BaseDirectory, "Content", "LegacyMims", "BuildB"),
            Path.Combine(AppContext.BaseDirectory, "Content", "LegacyMims", "ReportSkill"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Jibo.Cloud",
                "dotnet",
                "src",
                "Jibo.Cloud.Infrastructure",
                "Content",
                "LegacyMims",
                "BuildA")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Jibo.Cloud",
                "dotnet",
                "src",
                "Jibo.Cloud.Infrastructure",
                "Content",
                "LegacyMims",
                "BuildB")),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "Jibo.Cloud",
                "dotnet",
                "src",
                "Jibo.Cloud.Infrastructure",
                "Content",
                "LegacyMims",
                "ReportSkill"))
        };

        return candidates.Where(Directory.Exists).ToArray();
    }
}