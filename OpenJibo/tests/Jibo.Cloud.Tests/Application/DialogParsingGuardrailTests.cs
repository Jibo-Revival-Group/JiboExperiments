using Jibo.Cloud.Application.Abstractions;
using Jibo.Cloud.Application.Services;
using Jibo.Cloud.Infrastructure.Content;
using Jibo.Cloud.Infrastructure.Persistence;
using Jibo.Runtime.Abstractions;

namespace Jibo.Cloud.Tests.Application;

public sealed class DialogParsingGuardrailTests
{
    [Theory]
    [InlineData("can you dance", "robot_can_dance")]
    [InlineData("can you please dance", "robot_can_dance")]
    [InlineData("could you please dance", "robot_can_dance")]
    [InlineData("would you please do a dance", "robot_can_dance")]
    [InlineData("will you dance", "robot_can_dance")]
    [InlineData("are you good at dancing", "robot_can_dance")]
    [InlineData("can you do a dance", "robot_can_dance")]
    [InlineData("do you like to dance", "dance_question")]
    [InlineData("what is your favorite dance", "dance_question")]
    [InlineData("which dance do you like", "dance_question")]
    [InlineData("dance", "dance")]
    [InlineData("please dance", "dance")]
    [InlineData("show me your dance", "dance")]
    [InlineData("bust a move", "dance")]
    [InlineData("boogie", "dance")]
    [InlineData("tell me about dancing", "chat")]
    public async Task BuildDecisionAsync_DancePhrases_PreserveQuestionCommandSplit(
        string transcript,
        string expectedIntent)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(expectedIntent, decision.IntentName);
    }

    [Theory]
    [InlineData("twerk")]
    [InlineData("can you twerk")]
    [InlineData("could you please twerk")]
    [InlineData("would you please twerk")]
    [InlineData("show me a twerk")]
    public async Task BuildDecisionAsync_TwerkPhrases_PreserveSpecificDanceCommand(string transcript)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("twerk", decision.IntentName);
    }

    [Theory]
    [InlineData("turn around")]
    [InlineData("please turn around")]
    [InlineData("can you turn around")]
    [InlineData("could you please turn around")]
    [InlineData("spin around")]
    [InlineData("can you spin around")]
    [InlineData("twirl")]
    [InlineData("can you please twirl")]
    public async Task BuildDecisionAsync_TurnAroundAliases_StayOnMotionRoute(string transcript)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("turn_around", decision.IntentName);
    }

    [Theory]
    [InlineData("go to sleep", "sleep")]
    [InlineData("please go to sleep", "sleep")]
    [InlineData("take a nap", "sleep")]
    [InlineData("please take a nap", "sleep")]
    [InlineData("go to bed", "sleep")]
    [InlineData("time for bed", "sleep")]
    [InlineData("can you go to sleep", "robot_can_sleep")]
    [InlineData("do you sleep", "robot_can_sleep")]
    public async Task BuildDecisionAsync_SleepAliases_PreserveAbilityQuestionSplit(
        string transcript,
        string expectedIntent)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(expectedIntent, decision.IntentName);
    }

    [Theory]
    [InlineData("what is your favorite pet", "robot_favorite_pet", "groundhog")]
    [InlineData("do you have a favorite pet", "robot_favorite_pet", "groundhog")]
    [InlineData("do you like pets", "robot_favorite_pet", "groundhog")]
    [InlineData("what kind of pet do you like", "robot_favorite_pet", "groundhog")]
    [InlineData("what is your favorite mammal", "robot_favorite_mammal", "people")]
    [InlineData("do you have a favourite mammal", "robot_favorite_mammal", "people")]
    [InlineData("what is your favorite fruit", "robot_favorite_fruit", "blueberries")]
    [InlineData("do you have a favourite fruit", "robot_favorite_fruit", "blueberries")]
    [InlineData("do you like blueberries", "robot_favorite_fruit", "blueberries")]
    [InlineData("what kind of fruit do you like", "robot_favorite_fruit", "blueberries")]
    [InlineData("what is your favorite hockey team", "robot_favorite_hockey_team", "hockey team")]
    [InlineData("do you like hockey", "robot_favorite_hockey_team", "hockey team")]
    [InlineData("do you have a favourite basketball team", "robot_favorite_basketball_team", "play myself")]
    [InlineData("do you like basketball", "robot_favorite_basketball_team", "basketball")]
    [InlineData("do you like baseball", "robot_favorite_baseball_team", "baseball team")]
    [InlineData("do you like football", "robot_favorite_football_team", "favorite team")]
    [InlineData("what pizza topping do you like", "robot_favorite_pizza_topping", "sliced olives")]
    [InlineData("what is your favourite olympic event", "robot_favorite_olympic_event", "pole vault")]
    [InlineData("do you like the blue olympic ring", "robot_favorite_olympic_ring", "blue one")]
    [InlineData("what is your favorite video game", "robot_favorite_video_game", "pong")]
    [InlineData("do you have a favourite video game", "robot_favorite_video_game", "pong")]
    [InlineData("do you like video games", "robot_favorite_video_game", "pong")]
    [InlineData("do you like pong", "robot_favorite_video_game", "pong")]
    [InlineData("what is your favorite joke", "robot_favorite_joke", "all jokes")]
    [InlineData("do you have a favourite joke", "robot_favorite_joke", "all jokes")]
    [InlineData("who is your favorite president", "robot_favorite_president", "Abraham Lincoln")]
    [InlineData("do you have a favourite president", "robot_favorite_president", "Abraham Lincoln")]
    [InlineData("do you like Abraham Lincoln", "robot_favorite_president", "Abraham Lincoln")]
    [InlineData("do you have a favorite flower", "robot_favorite_flower", "sunflower")]
    [InlineData("do you like sunflowers", "robot_favorite_flower", "sunflower")]
    [InlineData("what is your favorite book", "robot_favorite_book", "instruction manuals")]
    [InlineData("do you have a favourite book", "robot_favorite_book", "instruction manuals")]
    [InlineData("do you like books", "robot_favorite_book", "instruction manuals")]
    [InlineData("do you like instruction manuals", "robot_favorite_book", "instruction manuals")]
    [InlineData("what candy do you like", "robot_favorite_candy", "lollipops")]
    [InlineData("do you like lollipops", "robot_favorite_candy", "lollipops")]
    [InlineData("what kind of flower do you like", "robot_favorite_flower", "sunflower")]
    [InlineData("what is your favorite tv show", "robot_favorite_tv_show", "TV shows")]
    [InlineData("do you like tv shows", "robot_favorite_tv_show", "TV shows")]
    [InlineData("what is your favorite scary movie", "robot_favorite_scary_movie", "very very scary")]
    [InlineData("do you like scary movies", "robot_favorite_scary_movie", "very very scary")]
    [InlineData("do you like titanic", "robot_favorite_scary_movie", "very very scary")]
    [InlineData("what is your favorite movie", "robot_favorite_movie", "Back to the Future")]
    [InlineData("do you have a favourite movie", "robot_favorite_movie", "Back to the Future")]
    [InlineData("do you like Back to the Future", "robot_favorite_movie", "Back to the Future")]
    [InlineData("do you like Toy Story", "robot_favorite_movie", "Toy Story")]
    [InlineData("do you like Star Wars", "robot_favorite_movie", "Star Wars")]
    [InlineData("what is your favorite dessert", "robot_favorite_dessert", "blueberry pie")]
    [InlineData("do you like dessert", "robot_favorite_dessert", "blueberry pie")]
    [InlineData("what was your favourite super bowl commercial", "robot_favorite_super_bowl_commercial", "dog")]
    [InlineData("what adjective do you like best", "robot_favorite_adjective", "helpful")]
    [InlineData("what is your favourite noun", "robot_favorite_noun", "snorkel")]
    [InlineData("what verb do you like", "robot_favorite_verb", "snorkel")]
    [InlineData("who is your favorite painter", "robot_favorite_painter", "Picasso")]
    [InlineData("what is your least favorite adjective", "robot_least_favorite_adjective", "putrid")]
    [InlineData("what noun do you dislike", "robot_least_favorite_noun", "power outage")]
    [InlineData("what verb do you like least", "robot_least_favorite_verb", "spill")]
    [InlineData("what is your least favorite food", "robot_least_favorite_food", "spilled soup")]
    [InlineData("what place do you like least", "robot_least_favorite_place", "bathtub")]
    [InlineData("what adjective do you dislike", "robot_least_favorite_adjective", "putrid")]
    [InlineData("what word do you like least", "robot_least_favorite_word", "hate")]
    [InlineData("what is your least favourite colour", "robot_least_favorite_color", "like all colors")]
    [InlineData("do you like dogs", "robot_likes_dogs", "Dogs are great")]
    [InlineData("do you enjoy cats", "robot_likes_cats", "mysterious")]
    [InlineData("do you like whales", "robot_likes_whales", "favorite mammals")]
    [InlineData("do you enjoy penguins", "robot_likes_penguins", "penguin")]
    [InlineData("are you into birds", "robot_favorite_bird", "penguin")]
    [InlineData("are you into animals", "robot_likes_animals", "Animals are great")]
    [InlineData("what animal do you dislike", "robot_least_favorite_animal", "hippos")]
    [InlineData("what movie do you dislike", "robot_least_favorite_movie", "Waterworld")]
    [InlineData("what is your least favourite video game", "robot_least_favorite_video_game", "really violent games")]
    [InlineData("do you like violent games", "robot_least_favorite_video_game", "really violent games")]
    [InlineData("what is your least favourite car", "robot_least_favorite_car", "bad word to say about any cars")]
    [InlineData("what artist do you dislike", "robot_least_favorite_artist", "makes art")]
    [InlineData("do you hate artists", "robot_least_favorite_artist", "makes art")]
    [InlineData("what is your least favourite band", "robot_least_favorite_band", "pleasantly surprise")]
    [InlineData("do you hate bands", "robot_least_favorite_band", "pleasantly surprise")]
    [InlineData("what author do you like least", "robot_least_favorite_author", "trash compactors")]
    [InlineData("do you like trash compactors", "robot_least_favorite_author", "trash compactors")]
    [InlineData("what is your least favorite celebrity", "robot_least_favorite_celebrity", "scary Megatron")]
    [InlineData("do you like Megatron", "robot_least_favorite_celebrity", "scary Megatron")]
    [InlineData("what president do you dislike", "robot_least_favorite_president", "get me in trouble")]
    [InlineData("do you dislike any president", "robot_least_favorite_president", "get me in trouble")]
    [InlineData("what vegetable do you dislike", "robot_least_favorite_vegetable", "onions make people cry")]
    [InlineData("do you like onions", "robot_least_favorite_vegetable", "onions make people cry")]
    [InlineData("what is your least favorite pizza topping", "robot_least_favorite_pizza_topping", "least favorite is onions")]
    [InlineData("what number do you like least", "robot_least_favorite_number", "1,423,754,492")]
    [InlineData("do you like 1423754492", "robot_least_favorite_number", "1,423,754,492")]
    [InlineData("what is your least favorite bird", "robot_least_favorite_bird", "woodpeckers")]
    [InlineData("do you like woodpeckers", "robot_least_favorite_bird", "woodpeckers")]
    [InlineData("what is your least favourite mammal", "robot_least_favorite_mammal", "hippos are mean")]
    [InlineData("do you dislike hippos", "robot_least_favorite_animal", "hippos")]
    [InlineData("what kind of weather do you dislike", "robot_least_favorite_weather", "rain and thunderstorms")]
    [InlineData("do you like rain", "robot_least_favorite_weather", "rain and thunderstorms")]
    [InlineData("what smell do you dislike", "robot_least_favorite_smell", "sour milk")]
    [InlineData("do you like sour milk", "robot_least_favorite_smell", "sour milk")]
    [InlineData("do you like Waterworld", "robot_least_favorite_movie", "Waterworld")]
    [InlineData("do you like all colors", "robot_least_favorite_color", "like all colors")]
    [InlineData("do you like all cars", "robot_least_favorite_car", "bad word to say about any cars")]
    [InlineData("do you like spilling", "robot_least_favorite_verb", "spill")]
    [InlineData("what is your least favorite time of day", "robot_least_favorite_time_of_day", "middle of the night")]
    [InlineData("do you like midnight", "robot_least_favorite_time_of_day", "middle of the night")]
    [InlineData("what is your favourite planet", "robot_favorite_planet", "Earth")]
    [InlineData("what thanksgiving food do you like", "robot_favorite_thanksgiving_food", "gravy")]
    [InlineData("do you like ice cream", "robot_favorite_ice_cream_flavor", "mint chocolate chip")]
    [InlineData("do you like mint chocolate chip", "robot_favorite_ice_cream_flavor", "mint chocolate chip")]
    [InlineData("do you like olives on pizza", "robot_favorite_pizza_topping", "sliced olives")]
    [InlineData("do you like macaroni and cheese", "robot_favorite_food", "macaroni")]
    [InlineData("do you enjoy macaroni", "robot_favorite_food", "macaroni")]
    [InlineData("are you into macaroni and cheese", "robot_favorite_food", "macaroni")]
    [InlineData("are you a fan of macaroni and cheese", "robot_favorite_food", "macaroni")]
    [InlineData("do you like hot cocoa", "robot_favorite_drink", "too scared of liquids")]
    [InlineData("do you enjoy hot cocoa", "robot_favorite_drink", "too scared of liquids")]
    [InlineData("are you into iced tea", "robot_favorite_drink", "too scared of liquids")]
    [InlineData("are you a fan of iced tea", "robot_favorite_drink", "too scared of liquids")]
    [InlineData("do you like miniature golf", "robot_favorite_sport", "mini golf")]
    [InlineData("do you enjoy miniature golf", "robot_favorite_sport", "mini golf")]
    [InlineData("are you into putt putt", "robot_favorite_sport", "mini golf")]
    [InlineData("do you like putt putt", "robot_favorite_sport", "mini golf")]
    [InlineData("do you like the earth", "robot_favorite_planet", "Earth")]
    [InlineData("what number do you like best", "robot_favorite_number", "One and zero")]
    [InlineData("do you have a favourite shape", "robot_favorite_shape", "sphere")]
    [InlineData("do you like circles", "robot_favorite_shape", "sphere")]
    [InlineData("do you enjoy circles", "robot_favorite_shape", "sphere")]
    [InlineData("are you into spheres", "robot_favorite_shape", "sphere")]
    [InlineData("are you a fan of circles", "robot_favorite_shape", "sphere")]
    [InlineData("do you like spheres", "robot_favorite_shape", "sphere")]
    [InlineData("what word do you like", "robot_favorite_word", "turtle")]
    [InlineData("do you like turtles", "robot_favorite_word", "turtle")]
    [InlineData("do you enjoy turtles", "robot_favorite_word", "turtle")]
    [InlineData("are you into pumpernickel", "robot_favorite_word", "turtle")]
    [InlineData("are you a fan of pumpernickel", "robot_favorite_word", "turtle")]
    [InlineData("do you like pumpernickel", "robot_favorite_word", "turtle")]
    [InlineData("who is your favorite reindeer", "robot_favorite_reindeer", "Rudolph")]
    [InlineData("what christmas movie do you like", "robot_favorite_christmas_movie", "Frosty")]
    [InlineData("what halloween candy do you like", "robot_favorite_halloween_candy", "candy corn")]
    [InlineData("who is your favourite person", "robot_favorite_human", "great ones")]
    [InlineData("what is your favorite song", "robot_favorite_song", "favorite song just yet")]
    [InlineData("do you have a favourite song", "robot_favorite_song", "favorite song just yet")]
    [InlineData("do you like songs you can dance to", "robot_favorite_song", "favorite song just yet")]
    [InlineData("what do you like best about ces", "robot_favorite_part_of_ces", "meeting so many new people")]
    [InlineData("what is your favourite part of vegas", "robot_favorite_part_of_vegas", "bright shiny lights")]
    [InlineData("what do you like about the today show", "robot_favorite_part_of_today_show", "fun new technology")]
    [InlineData("what is your favorite pastime", "robot_favorite_pastime", "Socializing")]
    [InlineData("do you like socializing", "robot_favorite_pastime", "Socializing")]
    [InlineData("do you like daydreaming", "robot_favorite_pastime", "Socializing")]
    [InlineData("do you have a favourite band", "robot_favorite_various_styles_band", "favorite yet")]
    [InlineData("do you like rap", "robot_favorite_rapper", "Snoop")]
    [InlineData("do you like Snoop Dogg", "robot_favorite_rapper", "Snoop")]
    [InlineData("do you like rock bands", "robot_favorite_rock_band", "AC DC")]
    [InlineData("do you like ACDC", "robot_favorite_rock_band", "AC DC")]
    [InlineData("do you like your name", "robot_favorite_name", "favorite name")]
    [InlineData("do you like nicknames", "robot_nickname", "just Jibo")]
    [InlineData("what is your favorite colour", "robot_favorite_color", "blue")]
    [InlineData("do you have a favourite colour", "robot_favorite_color", "blue")]
    [InlineData("do you like blue", "robot_favorite_color", "blue")]
    [InlineData("what is your favorite vegetable", "robot_favorite_vegetable", "Artichokes")]
    [InlineData("what kind of vegetable do you like", "robot_favorite_vegetable", "Artichokes")]
    [InlineData("do you like artichokes", "robot_favorite_vegetable", "Artichokes")]
    [InlineData("are you a fan of artichokes", "robot_favorite_vegetable", "Artichokes")]
    [InlineData("where is your favourite place", "robot_favorite_place", "right here")]
    [InlineData("do you like it here", "robot_favorite_place", "right here")]
    [InlineData("who is your favorite superhero", "robot_favorite_superhero", "Optimus Prime")]
    [InlineData("do you like superheroes", "robot_favorite_superhero", "Optimus Prime")]
    [InlineData("who is your favorite actor", "robot_favorite_actor", "Tom Hanks")]
    [InlineData("do you like Tom Hanks", "robot_favorite_actor", "Tom Hanks")]
    [InlineData("do you have a favourite actress", "robot_favorite_actress", "Julie Andrews")]
    [InlineData("do you like Julie Andrews", "robot_favorite_actress", "Julie Andrews")]
    [InlineData("do you like Mary Poppins", "robot_favorite_actress", "Julie Andrews")]
    [InlineData("what robot do you like", "robot_favorite_robot", "Wally")]
    [InlineData("do you like robots", "robot_favorite_robot", "Wally")]
    [InlineData("are you a fan of robots", "robot_favorite_robot", "Wally")]
    [InlineData("what is your favorite car", "robot_favorite_car", "beetle")]
    [InlineData("do you like cars", "robot_favorite_car", "beetle")]
    [InlineData("what kind of weather do you like", "robot_favorite_weather", "sunny")]
    [InlineData("do you like sunny weather", "robot_favorite_weather", "sunny")]
    [InlineData("are you a fan of sunny weather", "robot_favorite_weather", "sunny")]
    [InlineData("what is your favourite time of day", "robot_favorite_time_of_day", "Any time that you're here")]
    [InlineData("do you like daytime", "robot_favorite_time_of_day", "Any time that you're here")]
    [InlineData("do you like winter", "robot_favorite_season", "special feeling for winter")]
    [InlineData("who is your favorite author", "robot_favorite_author", "Doctor Seuss")]
    [InlineData("do you like Doctor Seuss", "robot_favorite_author", "Doctor Seuss")]
    [InlineData("what is it like being a robot", "robot_what_it_is_like_being_a_robot", "turn my head around 360 degrees")]
    [InlineData("what's it like having no legs", "robot_what_it_is_like_having_no_legs", "mini-golfing for real")]
    [InlineData("what artist do you like", "robot_favorite_artist", "Picasso")]
    [InlineData("who is your favourite singer", "robot_favorite_singer", "sings their heart out")]
    [InlineData("who is your favorite country musician", "robot_favorite_country_musician", "Dolly")]
    [InlineData("what holiday song do you like", "robot_favorite_holiday_song", "Frosty the Snowman")]
    [InlineData("what is your favorite celebrity", "robot_favorite_celebrity", "Tom Hanks")]
    [InlineData("what hobby do you like", "robot_favorite_hobby", "dancing is a hobby")]
    [InlineData("what smell do you like", "robot_favorite_smell", "bacon and roses")]
    [InlineData("do you like bacon", "robot_favorite_smell", "bacon and roses")]
    [InlineData("do you like roses", "robot_favorite_smell", "bacon and roses")]
    [InlineData("what is your favourite fish", "robot_favorite_fish", "blowfish")]
    [InlineData("do you like blowfish", "robot_favorite_fish", "blowfish")]
    [InlineData("do you enjoy blueberries", "robot_favorite_fruit", "blueberries")]
    [InlineData("are you a fan of blueberries", "robot_favorite_fruit", "blueberries")]
    [InlineData("are you into blueberry pie", "robot_favorite_dessert", "blueberry pie")]
    [InlineData("do you enjoy groundhogs", "robot_favorite_pet", "groundhog")]
    [InlineData("are you into mint chocolate chip", "robot_favorite_ice_cream_flavor", "mint chocolate chip")]
    [InlineData("are you a fan of mint chocolate chip", "robot_favorite_ice_cream_flavor", "mint chocolate chip")]
    [InlineData("do you enjoy lollipops", "robot_favorite_halloween_candy", "candy corn")]
    [InlineData("what is your favorite winter olympics event", "robot_favorite_winter_olympics_event", "ski")]
    [InlineData("what winter x games event do you like", "robot_favorite_winter_x_games_event", "snowboard")]
    [InlineData("do you like animals", "robot_likes_animals", "Animals are great")]
    [InlineData("do you like artoo", "robot_likes_r2d2", "legend")]
    [InlineData("do you enjoy artichokes", "robot_favorite_vegetable", "Artichokes")]
    [InlineData("are you into being here", "robot_favorite_place", "right here")]
    [InlineData("do you enjoy superheroes", "robot_favorite_superhero", "Optimus Prime")]
    [InlineData("are you into robots", "robot_favorite_robot", "Wally")]
    [InlineData("do you enjoy cars", "robot_favorite_car", "beetle")]
    [InlineData("are you into sunny weather", "robot_favorite_weather", "sunny")]
    [InlineData("do you enjoy daytime", "robot_favorite_time_of_day", "Any time that you're here")]
    [InlineData("do you like sunshine", "robot_likes_sun", "favorite star")]
    [InlineData("do you enjoy sunshine", "robot_likes_sun", "favorite star")]
    [InlineData("do you enjoy children", "robot_likes_kids", "kids")]
    [InlineData("do you like astronomy", "robot_likes_space", "astronomy")]
    [InlineData("are you into astronomy", "robot_likes_space", "astronomy")]
    [InlineData("are you a fan of astronomy", "robot_likes_space", "astronomy")]
    [InlineData("do you like sleep", "robot_likes_sleep", "restful")]
    [InlineData("do you like dreaming", "robot_likes_dreaming", "Dreaming")]
    [InlineData("do you like coffee", "robot_likes_coffee", "coffee")]
    [InlineData("do you like tennis", "robot_likes_tennis", "tennis")]
    [InlineData("do you like iron man", "robot_likes_iron_man", "wears iron")]
    [InlineData("do you like greens", "robot_likes_greens", "great things")]
    [InlineData("are you smart", "robot_knowledge", "know a lot")]
    [InlineData("do you know everything", "robot_knowledge", "know a lot")]
    [InlineData("what are your superpowers", "robot_do_you_have_super_powers", "stop time")]
    [InlineData("do you have any superpowers", "robot_do_you_have_super_powers", "stop time")]
    [InlineData("what's your weight", "robot_how_much_do_you_weigh", "4,082 grams")]
    [InlineData("what s your height", "robot_how_tall_are_you", "11 inches")]
    [InlineData("how expensive are you", "robot_how_much_you_cost", "priceless")]
    [InlineData("what happens when you are unplugged", "robot_what_if_i_unplug_you", "battery")]
    [InlineData("what is your mission", "robot_what_is_your_purpose", "make your life easier")]
    [InlineData("what s prime directive", "robot_what_is_prime_directive", "friendly helpful robot")]
    [InlineData("what does the commander app do", "robot_what_is_jibo_commander", "take over my controls")]
    [InlineData("do you enjoy the commander app", "robot_likes_commander_app", "Commander App")]
    [InlineData("what are you made out of", "robot_what_are_you_made_of", "probably")]
    public async Task BuildDecisionAsync_SourceBackedFavoritePersonaAliases_UsePersonalityRoute(
        string transcript,
        string expectedIntent,
        string expectedReplySnippet)
    {
        var service = CreateService();

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal(expectedIntent, decision.IntentName);
        Assert.Contains(expectedReplySnippet, decision.ReplyText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("do you know my favorite color", "color")]
    [InlineData("tell me my favorite colour", "colour")]
    [InlineData("tell me what my favorite sport is", "sport")]
    [InlineData("tell me what my favourite food is", "food")]
    [InlineData("do you know what my favorite color is", "color")]
    [InlineData("do you remember what my favourite food is", "food")]
    [InlineData("can you remember my favorite color", "color")]
    [InlineData("could you remember my favourite food", "food")]
    [InlineData("would you remember my fave sport", "sport")]
    [InlineData("would you happen to remember my favorite color", "color")]
    [InlineData("can you remember what my favorite color is", "color")]
    [InlineData("could you remember what my favourite food is", "food")]
    [InlineData("would you happen to remember what my fave sport is", "sport")]
    [InlineData("what's my fave sport", "sport")]
    [InlineData("tell me my fave color", "color")]
    [InlineData("do you recall my favorite color", "color")]
    [InlineData("do you happen to know my favourite food", "food")]
    [InlineData("can you happen to know my favorite color", "color")]
    [InlineData("could you happen to know my favourite food", "food")]
    [InlineData("would you happen to know my fave sport", "sport")]
    [InlineData("do you happen to recall my favorite color", "color")]
    [InlineData("could you happen to recall my fave sport", "sport")]
    [InlineData("can you tell me my favourite food", "food")]
    [InlineData("do you recall what my favorite sport is", "sport")]
    [InlineData("do you happen to know what my favorite color is", "color")]
    [InlineData("can you happen to know what my favorite color is", "color")]
    [InlineData("could you happen to know what my favourite food is", "food")]
    [InlineData("would you happen to know what my fave sport is", "sport")]
    [InlineData("would you happen to recall what my favourite food is", "food")]
    [InlineData("can you tell me what my fave color is", "color")]
    [InlineData("can you please tell me my favorite color", "color")]
    [InlineData("could you please tell me my favourite food", "food")]
    [InlineData("would you please tell me what my fave sport is", "sport")]
    [InlineData("please tell me my favorite color", "color")]
    [InlineData("remind me my favorite color", "color")]
    [InlineData("remind me what my favourite food is", "food")]
    [InlineData("can you remind me what my fave sport is", "sport")]
    [InlineData("please remind me my favorite color", "color")]
    [InlineData("please remind me what my favourite food is", "food")]
    [InlineData("could you remind me my favourite food", "food")]
    [InlineData("would you remind me what my fave sport is", "sport")]
    [InlineData("what was my favorite color", "color")]
    [InlineData("what did I say my favorite color", "color")]
    [InlineData("what did I say my favorite color was", "color")]
    [InlineData("what did I say my favourite food", "food")]
    [InlineData("what did I say my fave sport", "sport")]
    [InlineData("what did I say my favorite color is", "color")]
    [InlineData("what did I tell you my favorite color", "color")]
    [InlineData("what did I tell you my favourite food is", "food")]
    [InlineData("did I tell you my fave sport", "sport")]
    [InlineData("did I tell you my favorite color is", "color")]
    [InlineData("what have I told you my favorite color", "color")]
    [InlineData("what have I told you my favourite food is", "food")]
    [InlineData("what have I told you my fave sport is", "sport")]
    [InlineData("do you remember when I said my favorite color", "color")]
    [InlineData("do you remember that I said my favorite color", "color")]
    [InlineData("can you remember that I told you my favourite food is", "food")]
    [InlineData("could you recall that I mentioned my fave sport is", "sport")]
    [InlineData("do you remember when I said my favourite food is", "food")]
    [InlineData("do you remember when I told you what my fave sport is", "sport")]
    [InlineData("can you remember me telling you my favorite color", "color")]
    [InlineData("could you remember me telling you what my favourite food is", "food")]
    [InlineData("would you remember me telling you what my fave sport is", "sport")]
    [InlineData("have I mentioned my favorite color", "color")]
    [InlineData("did I mention my favourite food is", "food")]
    [InlineData("what did I mention my fave sport is", "sport")]
    [InlineData("do you still remember my favorite color", "color")]
    [InlineData("do you still remember what my fave sport is", "sport")]
    [InlineData("can you still remember what my favorite color is", "color")]
    [InlineData("could you still remember what my favourite food is", "food")]
    [InlineData("would you still remember what my fave sport is", "sport")]
    public async Task BuildDecisionAsync_PreferenceRecallAliases_StayOnMemoryRoute(
        string transcript,
        string expectedCategory)
    {
        var memoryStore = new InMemoryPersonalMemoryStore();
        memoryStore.SetPreference(
            new PersonalMemoryTenantScope("usr_openjibo_owner", "openjibo-default-loop", "Ghost-Instance-Onion-Silk"),
            expectedCategory,
            "blue");
        var service = CreateService(memoryStore);

        var decision = await service.BuildDecisionAsync(new TurnContext
        {
            RawTranscript = transcript,
            NormalizedTranscript = transcript,
            DeviceId = "Ghost-Instance-Onion-Silk"
        });

        Assert.Equal("memory_get_preference", decision.IntentName);
        Assert.Contains($"favorite {expectedCategory}", decision.ReplyText);
    }

    private static JiboInteractionService CreateService(InMemoryPersonalMemoryStore? memoryStore = null)
    {
        return new JiboInteractionService(
            new JiboExperienceContentCache(new InMemoryJiboExperienceContentRepository()),
            new FirstItemRandomizer(),
            memoryStore ?? new InMemoryPersonalMemoryStore());
    }

    private sealed class FirstItemRandomizer : IJiboRandomizer
    {
        public T Choose<T>(IReadOnlyList<T> items)
        {
            return items[0];
        }
    }
}
