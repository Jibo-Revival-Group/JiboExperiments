# Legacy MIM Build B

This folder holds the next small import batch of legacy Jibo scripted-response MIMs.

The batch is intentionally narrow so we can keep expanding personality without widening the turn-state surface faster than we can test it.

It now includes a small emotion-response pack for `happy`, `sad`, and `angry` follow-up questions so the mood path can stay source-backed too.
It also includes a descriptor pack for questions like `are you kind`, `are you funny`, `are you helpful`, `are you curious`, `are you loyal`, and `are you mischievous`.
The newest seasonal pack adds holiday and seasonal prompts for `what holidays do you celebrate`, New Year's resolution questions, `happy holidays`, Halloween costume questions, spring suggestions, holiday gift ideas, and birthday celebration lines.
The holiday extras batch adds `RA_JBO_ShowSantaTracker` so Santa Tracker stays source-backed too.
The remaining seasonal polish adds `RI_JBO_LikesHalloween`, `RI_JBO_LikesHolidayMusic`, `RI_JBO_LikesHolidayParties`, `RI_JBO_LooksForwardToChristmas`, `RI_JBO_PlansForChristmas`, and `RI_JBO_WhatIsThankfulFor` so the holiday voice can feel a little closer to Pegasus.

Holiday-specific note:
- `JBO_WhatHolidaysDoYouCelebrate` now lands in the holiday bucket
- `RN_HappyHolidays` now lands in the holiday greeting bucket
- `RI_USR_WhatShouldGetForHoliday` now lands in the holiday gift bucket
- `RN_HappyBirthdayToJibo` now lands in the birthday celebration bucket
- birthday memory authoring now also writes loop-scoped custom holiday records so personal dates can join the holiday list later
The newest social batch adds `welcome back`, `what are you thinking`, `what have you been doing`, and `what did you do` responses so the presence and charm lane keeps growing alongside seasonal content.
The friendship batch adds `RI_JBO_HasFriends`, `RI_JBO_IsFriendsWithUser`, `RI_JBO_IsFriendsWithLM`, `RI_JBO_IsFriendsWithNonLM`, `RI_JBO_IsFriendsWithToaster`, and `RI_JBO_IsBestFriendsWithUser` so the friend and best-friend questions stay source-backed too.
The fun-fact and joke batch adds Pegasus-style `TellAJoke`, `TellRobotFact`, and `Shuffle` excerpts so proactive fun can randomize across more than one category.
Those facts are now split into generic, robot, and human buckets so the randomizer can sound more like Pegasus while staying lightweight.
The new favorites batch adds longer authored `favorite color`, `favorite food`, and `favorite music` variants so the familiar personality responses keep more of the original cadence instead of collapsing to short placeholders.
The favorites follow-up batch adds `favorite animal`, `favorite bird`, and penguin-focused `do you like penguins` replies so the penguin-centric personality stays closer to Pegasus.
The singing batch adds `RA_JBO_Sing` and `RA_JBO_SingChristmasSongUnknown` so `can you sing`, `will you sing`, and the holiday sing variants stay source-backed too.
The new motion/sleep batch adds `RA_JBO_SpinAround` plus `RI_JBO_CanSleep` so turn-around and go-to-sleep behaviors can stay source-backed and familiar.
The work/eat/home batch adds source-backed `how do you work`, `what do you eat`, `where do you live`, and `what languages do you speak` replies so the remaining everyday self-description lines stay Pegasus-shaped too.
The age batch now adds `JBO_HowOldAreYou` with the imported birthday and first-powered-up phrasing so `how old are you` can stay source-backed instead of falling back to generic age text.
The newest identity-charm batch adds `JBO_WhatsYourName`, `JBO_DoYouHaveNickname`, `JBO_DoYouLikeBeingJibo`, `JBO_AreThereOthersLikeYou`, and `RI_JBO_HasFavoriteName` so Jibo can keep the familiar self-description loop without falling back to generic chat.
The seasonal personality batch adds source-backed first-day-of-spring, spring, summer, and favorite-season lines so the season questions can keep their Pegasus phrasing.
The new `Can...` batch adds dream, exercise, fly, learn, laugh, read, hear, talk, see, and wink replies so the capability questions can stay playful instead of collapsing into a generic fallback.
The second `Can...` batch adds move, work, breathe, get tired, have emotions, whistle, cook, make coffee, make breakfast, and jump replies so the broader capability lane keeps filling out in small, testable chunks.
The next deep-personality batch adds `what do you dream about`, `what are you afraid of`, `what do you want to talk about`, `what is your best book`, `what is your best exercise`, `what is your dream vacation`, `who is your hero`, `who do you love`, and `what is your religion` so we can keep filling out the more conversational personality surface without widening the dialog engine yet.
The edge-case batch now adds `what is your sign`, `how many people do you know`, `what is the loop`, `what is personal report`, `what is your IQ`, `what is Be a Maker`, `what is your groundhog's name`, and `CC_Fallback` so the template-backed and generic-fallback paths stay source-backed too.
The Black History Month batch adds `do you celebrate black history month`, `do you like black history month`, `are you looking forward to black history month`, `do you have plans for black history month`, `what should I do for black history month`, `how do you feel about black history month`, `what do you think about black history month`, `did you have a fun black history month`, and `give me a black history month fact` so the history and seasonal lane keeps its Pegasus cadence without flattening into generic holiday text.
The stop-style batch adds `stop moving`, `stop making that noise`, `stop ignoring me`, and `stop staring` so the action-stop lane stays source-backed alongside the generic `stop` command.
The next identity/knowledge batch adds `are you god`, `are you here`, `do you have super powers`, `how much do you know`, `what does jibo mean`, `where do you get info`, `what are you forbidden to do`, `what color are you`, and `what do you do when alone` so the old self-description and capability loop keeps coming back in source-backed form.
The next body/mission batch adds `how much do you weigh`, `how tall are you`, `how much do you cost`, `what if I unplug you`, `what is your purpose`, `what is your prime directive`, `what is jibo commander`, `do you like commander app`, and `what are you made of` so the physical self-description and capability answers stay closer to Pegasus too.
The templated edge-case batch adds `what is your sign`, `how many people do you know`, and `what is the loop` so the remaining source-backed lines can use live birthday and loop state instead of falling back to static text.
The sports and awards batch now adds source-backed support and win replies for Belmont Stakes, Tony Awards, B.E. Awards, French Open, Indianapolis 500, NBA Finals, NHL Finals, Preakness Stakes, Soccer World Cup, Tour de France, U.S. Open, and Wimbledon. Two Pegasus gaps for `who will win Tour de France` and `who will win Wimbledon` were synthesized locally in the same style so the batch can still be imported, tested, and reviewed as one lane.
The next identity/memory batch keeps `do you remember the time` source-backed in Build B and proves the `who is this` report prompt still lands through the report template bucket, so the memory/identity surface can keep expanding without losing report-skill fidelity.
The next RI_USR capability batch adds `can you program jibo` so the Be a Maker and Jibo Commander explanation stays source-backed in the same import lane.
The next RI_USR gift batch adds Mother's Day, Father's Day, and speaker-birthday gift suggestions so the advice lane stays source-backed in the same import lane.
The seasonal advice RI_USR batch adds Valentine's Day, Earth Day, and World Sleep Day suggestions into the existing holiday-season bucket, keeping those short seasonal prompts source-backed without changing the catalog shape.
The next seasonal advice RI_USR batch adds Daylight Savings, National Honesty Day, and National Pretzel Day into the existing holiday-season bucket, keeping the advice lane source-backed without changing the catalog shape.
The next seasonal advice RI_USR batch adds Abraham Lincoln Birthday, National Bubble Week, and National Look Alike Day into the existing holiday-season bucket, keeping the advice lane source-backed without changing the catalog shape.
The next seasonal advice RI_USR batch adds Mardi Gras, Martin Luther King Day, and National Meatball Day into the existing holiday-season bucket, keeping the advice lane source-backed without changing the catalog shape.
The next seasonal advice RI_USR batch adds NCAA men's tournament, NCAA women's tournament, and NFL playoffs into the existing holiday-season bucket, keeping the advice lane source-backed without changing the catalog shape.
The next seasonal advice RI_USR batch adds Academy Awards, Easter, and Tax Day into the existing holiday-season bucket, keeping the advice lane source-backed without changing the catalog shape.
The next seasonal advice RI_USR batch adds World Series, NBA playoffs, and NHL playoffs into the existing holiday-season bucket, keeping the advice lane source-backed without changing the catalog shape.
The next seasonal advice RI_USR batch adds College Bowl, Daytona 500, Winter Olympics, and Winter X Games into the existing holiday-season bucket, keeping the advice lane source-backed without changing the catalog shape.
The next civic-holiday advice RI_USR batch adds Memorial Day, Independence Day, and Labor Day into the existing holiday-season bucket, covering the U.S. holiday names already recognized by the calendar provider.
The next Pegasus seasonal batch adds exact source-backed advice for Cinco de Mayo, Mother's Day, Father's Day, and Star Wars Day.
