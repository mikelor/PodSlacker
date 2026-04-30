# Transcript: What's new with Gemini from Google DeepMind

**Source:** https://youtu.be/92LvgAcR6fI  
**Video ID:** `92LvgAcR6fI`

---

[00:00] [music]
[00:14] >> Hi everybody. My name is Michael Gressel
[00:16] I'm the VP of product management for the
[00:19] Gemini Enterprise Agent platform and I'm
[00:21] here today with with David Thacker and
[00:24] and Michele from from Replit. But I'll
[00:27] let them introduce themselves.
[00:29] Hi everyone. Great to see you all. I'm
[00:31] David Thacker VP of product at Google
[00:33] DeepMind based in California but I'm
[00:35] responsible for the product teams that
[00:37] are building our models and I also run
[00:39] DeepMind's applied AI team.
[00:42] Michele Catasta president and head of AI
[00:44] at Replit. In case you haven't heard of
[00:46] us yet, we are a platform to democratize
[00:49] software creation for everyone.
[00:52] Right?
[00:53] Great. I think today we've got a a bit
[00:56] of a combination panel presentation. I'm
[00:59] going to start off and give you just you
[01:00] know a glimpse of what we've been
[01:01] working on at DeepMind across the board
[01:03] and then I'll transition to Michael
[01:04] we'll talk about some of the things
[01:05] we're we're doing at Google Cloud to
[01:06] productize these models
[01:08] and then we'll hear from Replit on some
[01:10] of the ways that they're using our
[01:11] technology.
[01:13] So
[01:13] uh
[01:15] Here's us.
[01:17] Starting off at Google DeepMind. So I
[01:19] always like to start off by talking
[01:20] about the the mission of of DeepMind and
[01:22] and for those of you not familiar
[01:23] DeepMind was a startup founded in London
[01:25] back in 2010 with the mission to build
[01:27] artificial general intelligence and back
[01:29] in 2010 you know nobody else was was
[01:32] doing that. It it was truly science
[01:33] fiction back then
[01:35] but the company was really committed to
[01:36] this vision and in 2014 Google acquired
[01:38] the company
[01:40] and pretty much left it alone for the
[01:41] first five or six years and then really
[01:43] over the last three years we've brought
[01:44] DeepMind into the fold of Google. We now
[01:46] call it the engine room at Google. We've
[01:48] centralized AI model development for
[01:51] Google within DeepMind but we're still
[01:53] run very much like a startup within
[01:55] Google. Our founder and CEO still runs
[01:56] the company and I'll take you through
[01:59] some of the things we've been working on
[02:00] recently but I think one of the things
[02:02] we're most proud of is is Gemini or
[02:04] Gemini models which is just over two
[02:06] years old now. We're on our version 3.1
[02:10] of of the Gemini models and I I hope
[02:13] most of you are familiar with the
[02:14] lineup. It comes in a variety of
[02:16] different sizes everything from our
[02:17] largest most capable model which is our
[02:19] pro model which is great for powering
[02:21] agents. It's also great for coding for
[02:24] other types of stem or engineering math
[02:27] type work. We have our flash model in
[02:29] the middle which is a great blend of
[02:31] performance and efficiency. This is our
[02:32] most popular model and really our
[02:34] workhorse model and then finally we have
[02:36] our our flashlight model which is our
[02:38] smallest fastest most most performing
[02:40] model
[02:41] and I think what is really important to
[02:42] note is that
[02:44] at Google
[02:45] we're building these models to run
[02:46] across all the different Google
[02:47] products. My team works very closely
[02:49] with every every Google product team
[02:51] including Google Search, YouTube, Gmail,
[02:54] you name it they're all powered by
[02:55] Gemini today and the same models that we
[02:58] use to power Google products are the
[02:59] same models that we put into our
[03:01] our cloud platform for customers to use.
[03:04] Often times we're putting them into the
[03:05] cloud platform the same day we're
[03:06] launching them across Google product
[03:07] services.
[03:09] So you're getting the benefits of our of
[03:10] our intelligence at Google through the
[03:12] cloud platform.
[03:14] Gemini 3.1 we really focused in a few
[03:17] areas. I think where the model really
[03:18] shines and stands out is in its
[03:20] reasoning capabilities. Also its ability
[03:23] to to leverage multimodal understanding
[03:27] for for different types of activities.
[03:29] The model has an incredible ability to
[03:30] analyze video, text, audio, images,
[03:33] code. You know from the outset we
[03:35] designed Gemini to be a fully multimodal
[03:37] model.
[03:38] We also invested quite a bit in
[03:40] [clears throat] making the model work
[03:41] really well for powering agents and
[03:42] adjunctive type activities. So
[03:44] everything from you know complex
[03:46] multi-step planning to the ability to
[03:48] use various types of different tools
[03:49] across different environments. And
[03:51] finally coding has been a big focus for
[03:53] us
[03:54] at Gemini in making this model work
[03:56] incredibly well for coding for a variety
[03:57] of different reasons. You know within
[03:59] Google we use Gemini all of our software
[04:01] engineers tens of thousands of software
[04:02] engineers to
[04:04] to help develop our software but also
[04:06] coding is a foundational building block
[04:08] for a lot of different types of
[04:09] adjunctive activities.
[04:12] Beyond Gemini there's
[04:15] a number of other recent innovations
[04:17] we've had. I'm just going to go through
[04:18] a snapshot of what we're doing across
[04:19] the board. DeepMind Gemini is just one
[04:22] part of our portfolio. We're also
[04:24] heavily invested in areas as diverse as
[04:26] science models robotics models also
[04:30] generative media models. So I'll talk
[04:31] about a few of those things right now
[04:34] but one of our most recent launches is
[04:35] our lineup of open weight models called
[04:38] Gemma. So we're now on the the fourth
[04:40] iteration of Gemma. We just launched
[04:42] this a couple weeks ago.
[04:43] These are really incredible open source
[04:46] models that are you know derived from
[04:49] the Gemini 3 family of models and they
[04:51] come in a range of sizes everything from
[04:53] down to two billion parameters up to up
[04:56] to 30 billion parameters
[04:58] but really great for running on device
[05:01] in particular incredibly efficient.
[05:03] They're great for you know if you want
[05:05] to train a task specific model the Gemma
[05:07] models are are terrific for this.
[05:09] They support a wide range of languages.
[05:11] They have audio and video understanding
[05:13] built in so not just text and um
[05:16] we're really excited to see the adoption
[05:18] of these models since we've we've
[05:20] launched them.
[05:21] Another model capability we've really
[05:23] invested in is our audio and voice
[05:25] models. So we have a native audio model
[05:27] called Gemini Live. This allows us to
[05:29] take in audio tokens and output audio
[05:31] tokens. And so it's it's designed to
[05:34] power interactive voice experiences,
[05:36] voice agents. If you want to try this
[05:38] model you can use it in our Gemini
[05:40] consumer app. We just deployed this you
[05:42] know a few weeks ago and haven't rolled
[05:43] it out broadly
[05:45] but you know much reduced latency. We
[05:47] support things like expressiveness so
[05:49] you can really kind of steer and
[05:50] instruct the model to speak and act in a
[05:52] certain way. It also has the ability to
[05:56] react to the human it's talking to
[05:58] in terms of you know mirroring emotions
[06:00] and things like that. We we call that
[06:02] proactive audio.
[06:04] So we really think we're really bullish
[06:06] on voice as a modality to engage with
[06:09] with intelligence and with AI models and
[06:11] so it's been a big investment for us.
[06:15] On the generative media side we've got a
[06:16] full range of of models everything from
[06:18] image generation models to video
[06:20] generation models. Lyria is our is our
[06:22] music generation model. So we just
[06:24] launched Lyria 3 Pro. This allows you
[06:26] based on a text prompt or even image to
[06:28] generate a song up to three minutes long
[06:31] so a full song a full vocals.
[06:33] If you haven't tried this out I really
[06:35] encourage it it's super cool. You can
[06:37] you can find it in the Gemini consumer
[06:38] app
[06:39] but we think that um
[06:42] this is going to support a you know a
[06:43] range of different types of applications
[06:44] where where music is is really central
[06:46] to it.
[06:49] Just yesterday [clears throat] we we
[06:50] announced something called the the
[06:51] Gemini Deep Research Agent and so you
[06:52] one of the most popular things and use
[06:54] cases for Gemini we've seen has been
[06:56] using it for for deep exploratory
[06:58] research right? This could be for you
[07:00] know understanding the market landscape
[07:01] around something. With our deep research
[07:03] agent we now allow you with a single API
[07:05] call
[07:07] you can
[07:08] call deep research to not only access
[07:10] the web and the broader public
[07:11] information but you can also ground it
[07:13] on your own data sources to come back
[07:15] and not only will come back with with
[07:17] text but also can can generate charts
[07:19] and graphics infographics. And so we see
[07:22] a lot of adjunctive workflows where
[07:23] maybe the first step is you want to
[07:25] first you know generate some some
[07:27] research and understand the context of
[07:28] what you're dealing with and then from
[07:30] there on you might have subsequent steps
[07:32] in an adjunctive workflow but this is
[07:33] something that's been been really
[07:35] popular for us and we've been really
[07:36] excited about.
[07:39] We're also investing in world models at
[07:41] DeepMind. So we have a world model
[07:42] called Genie 3 which is our our latest
[07:44] model and again you can if you're a AI
[07:47] Ultra subscriber in the Gemini app you
[07:48] can try this out but starting with a
[07:50] text prompt or an image prompt you can
[07:53] that's some great music.
[07:55] >> [laughter]
[07:55] >> Sounds like Jump by Van Halen.
[07:58] Genie 3 allows you to create a fully
[08:00] interactive 3D world based on a simple
[08:03] text prompt or an image and you can
[08:05] navigate this world with your keyboard.
[08:07] You can decide you can you can basically
[08:08] prompt the model what type of character
[08:10] you want to be and you know world models
[08:12] are are very important for a number of
[08:15] different applications in AI. You know
[08:16] we think here
[08:18] you'll see applications in
[08:19] entertainment, in education,
[08:20] productivity,
[08:22] you know content generation,
[08:24] gaming but we also think it's very
[08:26] important for robotics. The ability to
[08:27] simulate the real world and how to have
[08:30] agents learn how to interact with that
[08:32] world. This is this is one of the things
[08:33] we're most excited about. And so finally
[08:36] wanted to mention Gemini robotics which
[08:37] is our robotics model. So robotics is
[08:39] really where physical world meets AI and
[08:43] um
[08:44] we've been investing heavily in in
[08:46] Gemini as a platform to power the next
[08:48] generation of robots general purpose
[08:50] robots that can reason through the world
[08:52] that can can use vision to understand
[08:55] what they're seeing and we've built the
[08:57] ability to actuate different types of
[08:59] robots. So we launched our latest
[09:01] robotics model last week. It's Gemini
[09:02] Robotics. The ER stands for embodied
[09:04] reasoning but it allows you to plug
[09:06] Gemini into a into a robot and it can
[09:08] leverage all of Gemini's reasoning and
[09:10] capabilities and vision capabilities to
[09:13] power different types of experiences. So
[09:14] you know we we have worked with a few
[09:16] different partners and and Boston
[09:17] Dynamics which is one of the leading
[09:19] hardware robotics companies
[09:21] is now using ER 1.6 to power its Spot
[09:24] robot which is a quadruped robot that
[09:26] can go around facility and investigate
[09:28] things. So with ER it's able to to count
[09:30] objects. It's able able to
[09:33] to read gauges and things like that that
[09:35] weren't possible before with with
[09:36] robotics. So again we're very excited
[09:38] about the future of robotics and
[09:41] this is just our latest model in that in
[09:43] that offering.
[09:48] Okay. My last slide here just want to
[09:49] talk a little bit about how we work with
[09:50] Google Cloud. You know at DeepMind we
[09:53] really want to see our AI transform
[09:55] every industry and every geography and
[09:57] we're bringing the same models, same
[09:58] Google models that we use the power
[10:00] Google products into our cloud platform.
[10:02] Um we work very closely with the Google
[10:03] Cloud team um because we love to get
[10:05] feedback from developers and customers
[10:07] and to see how these models are
[10:08] deployed. We're trying to build
[10:10] artificial general intelligence and so
[10:12] having a wide variety of diverse use
[10:13] cases for how these models are being
[10:14] used is really helpful for us um as we
[10:17] as we improve our models. And so uh
[10:18] Michael here, we we probably talk maybe
[10:21] once a day at least.
[10:21] >> Yeah, at least. And our team is much
[10:23] more
[10:23] >> Uh uh talking through uh and we've been
[10:25] really excited to see, you know, as soon
[10:26] as we launch a new model in the Google
[10:28] uh Cloud platform, it gets adoption
[10:30] right away, right? And it's it's really
[10:31] been amazing to see the different types
[10:33] of use cases. So I'm going to turn it
[10:34] over to to Michael to talk a little bit
[10:35] about how Google Cloud is deploying some
[10:37] of these capabilities. Thanks.
[10:41] Thanks, David. So at Google we we've
[10:44] been doing this for a very long time,
[10:46] right? Um DeepMind pioneered the
[10:48] technology, uh but we also have a uh in
[10:50] in Cloud, we have a global network that
[10:52] was uh originally intended to get a uh
[10:55] cat video from New York to to Tokyo in
[10:57] 75 milliseconds. Um but that accrues to
[10:59] the current moment because if you are a
[11:02] technician and you are working on an
[11:03] airplane and you are asking a field
[11:05] field manual for advice, you need that
[11:08] answer very very quickly as well. need
[11:09] to be able to use your hands and you
[11:11] need to be able to talk to your agents
[11:13] um rather than read that manual.
[11:15] Um we've also been working uh as as a
[11:18] hyperscaler on on reliability,
[11:20] obviously. Um but there are many other
[11:23] Google properties that accrue to this
[11:25] moment like like Google Maps, like uh
[11:27] Street View that allow us to do
[11:29] multimodal reasoning over different
[11:31] kinds of images as well. So the the
[11:34] advantage of of having 20 years of
[11:36] experience actually does accrue to the
[11:38] current moment in in many and and
[11:40] surprising ways.
[11:46] So
[11:47] there are are several problems that I'm
[11:50] I'm hearing um
[11:51] in general in agent building. Uh I've
[11:53] been launching models for about 2 years
[11:56] um since since I was actually at an one
[11:58] of our partners, Anthropic, where we
[12:00] launched um Claude 3. Uh
[12:02] my first model at Google was was Gemini
[12:05] 3. Um and what I what we're hearing is
[12:07] that um even Gemini 2.5, Gemini 3 are
[12:10] far more powerful than necessary to do a
[12:13] lot of enterprise workflows. It's still
[12:15] necessary to use 3.1 to write the best
[12:17] code in the world. There are many things
[12:19] that 3.1 is is leading on the frontier
[12:22] of intelligence, but for a bank's KYC
[12:25] process, they've been using um some of
[12:27] our models for for the past um several
[12:30] months in order to accomplish that goal.
[12:32] Or some of our clinical um research
[12:35] customers in in biotech, healthcare,
[12:37] life sciences have been doing
[12:38] statistical analysis on on on data sets,
[12:41] things like that. But not all of those
[12:43] customers are doing those things and
[12:44] we're seeing uptake day by day by day.
[12:46] And and that's because they needed a a
[12:48] platform that helped them implement
[12:50] common practices, right? The the agents
[12:53] come with intelligence built in. But
[12:55] that doesn't mean they know everything
[12:56] about your organizations. They don't
[12:58] know the background checks about your
[13:00] customers if you're running a KYC
[13:01] process. They don't know the locked
[13:03] database after your clinical study if
[13:06] you are trying to submit to the FDA and
[13:08] and show drug efficacy. So the platform
[13:11] allows access in scalable and flexible
[13:14] ways to that data. But um but
[13:17] contemplates credentialing and and and
[13:19] uh data access and information retrieval
[13:21] as well.
[13:22] And finally, it has to be built on
[13:24] trust, right? In order to take the human
[13:26] out of the loop and really um take
[13:28] advantage of cloud scale, you also need
[13:31] to be able to observe what the agent has
[13:33] done. You have to be able to audit uh
[13:35] the decisions it had made has made, the
[13:37] the data it has accessed, and what kind
[13:39] of synthetic generated data it has
[13:41] generated and at what level it has
[13:42] classified that data um for future
[13:45] storage and and retrieval. So it is only
[13:47] with um trust in front and trust at the
[13:51] end of that pipeline that the middle is
[13:53] going to scale without a human in the
[13:54] loop. Because if it's scaling with a
[13:55] human in the loop, it's really only
[13:57] scaling to the size of your teams and
[13:59] not the size of the cloud. Um you see it
[14:02] especially in the in the in the coding
[14:04] um realm, the SDLC really does
[14:07] contemplate a lot of those checks
[14:09] already, which is why you see such an
[14:11] enormous amount of code authorship done
[14:13] in the cloud at this point um by many of
[14:15] these enterprise um processes benefit as
[14:18] well.
[14:21] So I want to
[14:23] Sorry.
[14:25] Not how to use this thing. I want to
[14:26] move to a little bit of a conversation
[14:27] uh rather than slideware. Um and I want
[14:29] to start with something uh David said
[14:31] earlier. Our teams are are talking
[14:32] hundreds of times a day. We we sim ship
[14:34] almost every uh model that that a cloud
[14:37] application is going to use um maybe
[14:39] with the exception of uh
[14:41] embodied robotics. Um
[14:43] but but I want to ask you, how do you
[14:45] interact with cloud customers? How how
[14:47] does our relationship um not necessarily
[14:50] make cloud better, but how does it make
[14:51] our models better? How does it make them
[14:53] smarter, faster, um and and more
[14:55] efficient?
[14:56] Yeah, I I it's it's pretty amazing to
[14:58] see that there's now millions of
[15:00] developers that are building on on
[15:01] Gemini. And so uh with the Google Cloud
[15:03] team, when we're uh launching new
[15:05] models, when we're building new models,
[15:07] we'll oftentimes uh you know, run up a
[15:09] public preview or a private preview
[15:11] sometimes uh with a set of cloud
[15:13] customers to get feedback. And we've you
[15:15] know, selected a diversity of of
[15:16] different types of customers, different
[15:17] types of use cases, everything from
[15:19] small startups to to large enterprises.
[15:21] Um we'll use a lot of that feedback to
[15:23] understand what's working in the model,
[15:24] what's not working in the model. Um and
[15:26] we use that to to select which models
[15:27] we're going to launch. We also use it to
[15:29] uh to improve specific capabilities of
[15:31] the model. So it's been it's been super
[15:33] helpful to have that feedback. I think
[15:35] you know, it it's it's great to have um
[15:37] all the Google products using Gemini,
[15:39] but uh the the Google teams are kind of
[15:41] a captive audience. Like they have to
[15:42] use our models. Uh
[15:44] and so what's great about cloud
[15:45] customers is you all have choices. You
[15:46] know, you're looking at a a variety of
[15:47] different solutions. And so you know, we
[15:49] know to for you to choose our models, we
[15:51] really have to be uh the best at what
[15:52] you're trying to accomplish with the
[15:53] model. So um it really pushes us to make
[15:55] the the models better and and um you
[15:58] know, we've we've gotten some some great
[15:59] feedback from some of these deep
[16:00] partnerships we've had with with cloud.
[16:03] That's great.
[16:04] Um
[16:07] So you really recently released 3.1 Pro.
[16:09] Um we we get in discussions about hill
[16:11] climbing, about making numbers go up,
[16:13] right? Like there there are areas where
[16:15] we know that good enough is only good
[16:17] enough. Um but there are also areas
[16:20] where we focus uniquely um and think of
[16:23] us as having like specific um advantages
[16:26] in our models. How do you think about um
[16:28] some of those for for the audience?
[16:29] Where do you focus? Where you think
[16:31] other labs um
[16:32] you know, might not?
[16:34] Yeah, I I think you know, in terms of
[16:35] our models, you know, one of the
[16:36] advantages we have at Google is is we're
[16:38] completely full stack. So we have
[16:39] everything from our own TPUs, which you
[16:41] heard about earlier today, and all the
[16:42] innovations we're doing there. Um and
[16:44] the DeepMind team works very closely
[16:46] with our uh team working and building
[16:48] our chips. Uh we also work very closely
[16:50] with the cloud teams building our our
[16:51] next generation AI data centers uh to
[16:53] make sure those things are really well
[16:55] suited to not only train our models, but
[16:56] also run our models at scale. Um
[16:59] uh the other thing that we have is you
[17:01] know, with with Google products, our
[17:02] consumer products, uh you know, some of
[17:04] our products like Google Search,
[17:06] uh which has been completely reinvented
[17:07] with with Gemini. I mean, if you see
[17:09] Google Search now, it's got AI
[17:10] overviews, AI mode. Um and we're running
[17:13] that at over 2 billion plus user scale.
[17:16] Um so it's the most popular uh
[17:17] generative AI product in the world right
[17:19] now. And to do that and to be able to
[17:21] serve at very low latency to uh billions
[17:23] of consumers, like we have to make these
[17:24] models and the entire stack incredibly
[17:26] efficient. So that's one of the things
[17:28] we've done. And you you'll see like our
[17:29] you know, our our model, our flashlight
[17:31] model, which is our smallest uh Gemini
[17:33] model, that's the reason it's incredibly
[17:35] efficient, right? Because we've really
[17:36] tuned it over the years uh to work well
[17:38] at at massive Google scale and we can
[17:40] pass on those benefits to you. I think
[17:42] you know, a couple other areas where we
[17:43] really focus, you know, multimodality
[17:45] again, when when DeepMind
[17:48] uh started, you know, and when the
[17:50] Gemini project started, we wanted it to
[17:52] be natively multimodal from the outset.
[17:53] And part of the reason for that is when
[17:55] when you're trying to build AGI, we're
[17:57] trying to build an AI system that's
[17:58] going to pattern on on how the the human
[18:00] brain works and is able to do the types
[18:01] of cognitive tasks that a human can do.
[18:03] Uh and so it's really important for it
[18:05] to be multimodal. So not only on
[18:07] understanding, and if you haven't tried
[18:08] this, you should certainly try. You
[18:10] know, you can upload a video to to
[18:11] Gemini.
[18:12] Uh we could upload a video of us
[18:13] speaking on this panel and you could ask
[18:15] it to critique us and it would do a
[18:16] pretty incredible job. Uh and hopefully
[18:18] be pretty flattering.
[18:19] Uh but those are the types of things you
[18:21] can do with with the multimodal
[18:22] understanding capabilities. Um but we've
[18:24] also invested quite heavily in
[18:25] generation. So to generate images and
[18:28] videos and I we talked about music
[18:29] earlier. So you know, some of these are
[18:31] some of the areas where we're I think we
[18:33] think really uh Gemini shines. Yeah, I
[18:35] love I love certain areas where where
[18:37] we're able to to combine these things,
[18:39] right? Where we can do a grounding with
[18:40] Google Search and determine where you
[18:42] are. We can look up the weather for that
[18:44] location. But then we can produce a
[18:46] video of you um you know, in the rain to
[18:49] show you what it's going to be like for
[18:50] that day. And and so like pipelining and
[18:52] and combining these models is something
[18:53] that we see. Yeah.
[18:54] >> Exactly. Um now I get to ask you a
[18:57] couple questions.
[18:58] And then we're going to we're going to
[18:59] hear from Replit.
[19:00] Um so you know, one of the things with
[19:02] with these these models are amazing at
[19:04] helping uh the software engineering
[19:05] process and the and the software
[19:06] development life cycle. Um it's really
[19:08] speeding up things. So how do you when
[19:10] you're working with cloud customers, how
[19:11] do you help them understand not only
[19:14] what to build, but what not to build?
[19:16] Because in an era where it's becoming
[19:17] much easier with you know, AI code
[19:19] generation to build pretty much
[19:20] anything, um how do you how do you think
[19:22] about that? Uh
[19:24] that's actually a good question. That's
[19:25] a really fun one, actually. I I I was at
[19:27] a company called um
[19:28] Datadog for for 7 years. Um and some of
[19:31] you probably you have used my product.
[19:33] Um one of the things I liked there was
[19:35] that monitoring Kubernetes. And most of
[19:37] my customers looked like monitoring
[19:38] Kubernetes, whether it was like Capital
[19:40] One or Airbnb or or whatever, you know,
[19:42] it it's pods and clusters and and that
[19:44] sort of thing. Working with cloud
[19:46] customers on AI is extremely mission
[19:48] oriented, right? When when you work with
[19:50] somebody like Thomson Reuters, talking
[19:52] about points of law, you're talking
[19:54] about clauses in a contract and whether
[19:56] or not they they're better for the
[19:57] advocate or the adversary. Um when
[19:59] you're working with with a large bank,
[20:01] you're talking about their KYC process
[20:02] and anti-money laundering. Um
[20:05] when I talk to most of you, most of my
[20:07] most of the customers, um there's no
[20:10] easy answer for what what we should
[20:12] build with AI. Uh a lot of where I do
[20:15] focus is what do you know better than
[20:17] everybody else in the world? And and how
[20:20] can we help you trust a model? What I
[20:22] have seen, um well there there are three
[20:24] boundary conditions that that that I've
[20:26] seen on the models. One is this this
[20:28] frontier of intelligence. The best code
[20:30] in the world that is being written is
[20:32] really the least bad it's ever going to
[20:33] be at this point. But but we all know
[20:35] that we try to write better and better
[20:36] and better code when new and new models
[20:39] come out. But if you are are looking at
[20:41] a catalog
[20:42] and you want to determine if the green
[20:44] chairs are green and you're extracting
[20:45] metadata, that only gets so good. The
[20:47] green chairs don't get any greener,
[20:49] right? And so that doesn't require the
[20:51] the frontier of intelligence. But if
[20:52] you're a 911 operator, if you are a
[20:55] DoorDash delivery person, and you you
[20:57] you are trying to figure out how to
[20:59] apply a policy in real time in an urgent
[21:01] situation, you become very latency bound
[21:03] very very very quickly, right? And this
[21:05] is where um Gemini Flash shines because
[21:08] it brings the most intelligence to bear
[21:10] possible within a a fixed time budget.
[21:12] Um and then lastly, we were talking
[21:14] about flashlight a moment ago. If you
[21:16] want to moderate the entire internet,
[21:17] you're one of these platforms um that I
[21:19] won't necessarily name. Um you don't
[21:21] know how many posts there are, you don't
[21:22] know how bad they are going to be, and
[21:24] no matter how big your budget is, you
[21:25] have to contemplate an infinite volume.
[21:27] And and flashlight, because it was
[21:29] designed for Google Search, contemplates
[21:31] an infinite volume. So, we usually take
[21:33] that lens with our customers and
[21:34] understand what what their domains are
[21:37] um and and help them select the right
[21:38] tool for the job, whether or not they
[21:40] are doing something that is incredibly
[21:42] hard like validating a contract or or
[21:44] something that that happens at scale
[21:46] like moderating the internet, um we we
[21:48] work hand-in-hand with them. The other
[21:49] thing I will say is that uh the models
[21:52] are smarter than me in everything that I
[21:55] I don't that I'm not like 18 years of
[21:57] experience in. And I think that will
[21:59] most of us will find that to be the
[22:00] case. So, the other way we help
[22:03] customers is encouraging them not to
[22:05] tell the models what to do, how you
[22:07] think they should solve their problem,
[22:09] but really to give them the context that
[22:11] is necessary to solve the problem, and
[22:13] trust that they will as long as you uh
[22:16] tell them what your goal is, your your
[22:18] strategy, what your the context of your
[22:20] business might be. Just like hiring
[22:21] somebody, right? When you hire uh a
[22:23] brilliant intern or not even an intern,
[22:25] a new hire from Princeton with with 1
[22:28] year of experience or something. They're
[22:29] going to pick up what you're putting
[22:31] down pretty quickly, but that doesn't
[22:33] mean that if they came on to to Data Dog
[22:35] on day one they know how to do time
[22:36] series arithmetic. So, you know they're
[22:38] not going to do a very good job if you
[22:39] put them in front of a customer without
[22:41] that training. You're also not telling
[22:42] them exactly how to help a customer in
[22:44] every single situation. That's almost
[22:46] impossible. You're trusting that they
[22:47] will figure that out in the field and
[22:49] get better at it as they get more
[22:51] experienced. And the models and
[22:52] especially agents, the models are
[22:53] immutable obviously, but the agents are
[22:55] entirely capable of that kind of
[22:57] self-improvement and and and so the
[22:59] platform helps with that as well. And
[23:01] then and then related to that, I mean
[23:02] earlier today uh
[23:04] Google Cloud announced the Google
[23:06] Enterprise Agent Platform in the
[23:07] keynote. And so, can you tell us a
[23:09] little bit about that? Like how you're
[23:11] seeing customers deploy agents, what are
[23:13] the most common use cases, and any
[23:14] advice for building and scaling with
[23:16] that platform?
[23:16] >> So, choosing a model is just like one
[23:18] part of of building an agent, and then
[23:20] that part is very important, um but the
[23:22] agent platform contemplates much more
[23:24] than than the model garden itself. Um
[23:27] we separate this into like build uh
[23:30] scale, govern, and optimize. Um build
[23:33] and scale we've talked about a lot for
[23:34] for for many years. We we we offer a a
[23:37] ADK, we offer a
[23:39] agent engine. These are places that
[23:41] allow you to to build your harnesses, to
[23:43] deploy your harnesses, and give you an
[23:45] easy place to plumb observability into
[23:47] those harnesses. The very important
[23:49] parts that that that we're adding today
[23:51] um
[23:52] especially are are are govern and
[23:54] optimize, and those two things go
[23:55] hand-in-hand, right? Um we are able to
[23:58] watch what your agents are doing in real
[24:00] time. And like I was saying, your agents
[24:01] should self-improve even though the
[24:04] models themselves are are are immutable,
[24:06] you should be able to see in your stack
[24:08] traces um
[24:10] how how they are behaving in real time.
[24:12] And and that might mean stopping them
[24:14] from doing something because they've
[24:15] breached a policy that you don't want
[24:16] them to breach, or more commonly giving
[24:19] them examples of what they have done
[24:21] well or or poorly in the past, and
[24:23] putting that back into the context
[24:25] window. Context engineering isn't isn't
[24:27] a process of of like writing a lot of
[24:29] English with your hands. It's really
[24:31] just
[24:32] throwing a trace into the context window
[24:34] and say don't do that ever again, right?
[24:36] And it will extrapolate from there, and
[24:38] it'll get better and better and better
[24:40] from there. And so that that flywheel
[24:42] will both protect you, your customers,
[24:44] and your businesses, but it will also
[24:45] increase the the productivity of those
[24:48] agents for your businesses.
[24:50] Great. So, I think we should uh turn it
[24:53] over to to to Michele at at Replit um to
[24:56] talk about how they're using um the
[24:57] Gemini Enterprise Agent Platform and and
[25:00] and your business in general.
[25:02] Yeah.
[25:02] Um so, as I mentioned before, Replit is
[25:04] a bycoding platform that really
[25:06] democratizes access to software creation
[25:08] for everyone. And we've been using
[25:10] Gemini basically since since the
[25:11] inception of the of the project. And I
[25:14] think, you know, I want to give you
[25:15] public praise about really understanding
[25:18] that intelligence is not going to be
[25:20] just one-size-fits-all package. Um you
[25:23] know, you understood from the from day
[25:24] one that there should be like a powerful
[25:26] model that really pushes the frontier,
[25:28] but at the same time Flash and
[25:29] Flashlight are sort of like Swiss Army
[25:32] knives that you can deploy everywhere in
[25:33] product and add that touch of
[25:35] intelligence that makes the user
[25:37] experience way more delightful, but
[25:39] really doesn't break the bank, you know,
[25:40] that doesn't impact the the budget for
[25:42] for the users.
[25:43] And the the other advantage that we
[25:45] experience on a daily basis at Replit is
[25:47] the fact that Gemini is offered on
[25:49] Vertex. And you know, there is beauty in
[25:51] having everything in one single place,
[25:52] you know, our entire infrastructure runs
[25:54] on GCP. We get access to intelligence on
[25:56] Vertex as well, so that makes the cost
[25:59] and maintenance and like in general the
[26:01] work of our SREs much easier, you know,
[26:03] compared to spreading workloads across
[26:05] too many providers. Right.
[26:08] Um one of the I mean, what I love about
[26:10] Replit is that you really created an
[26:12] entire market of of engineers, right? Um
[26:16] it's not just like engineers who know
[26:17] the word Kubernetes, who hold the pager,
[26:19] that wake up at 3:00 in the morning.
[26:20] They're engineers now, we're all
[26:21] engineers now.
[26:22] >> Yeah, they don't even know what
[26:23] Kubernetes is. Exactly. Exactly. You
[26:25] don't need you don't need to know what a
[26:26] Kubernetes is to be an engineer anymore.
[26:28] Um
[26:28] >> So, it's it's a new market basically
[26:30] that we are charting together. And if
[26:32] you think about it, for decades we
[26:35] focused on making 30, 40, 50 million
[26:38] software developers in the world more
[26:39] productive. And now suddenly the
[26:41] aperture is much wider, we're targeting
[26:43] basically every knowledge worker in the
[26:44] world, probably 1.5 billion, 2 billion
[26:47] people in the world. So,
[26:48] very exciting journey ahead for us.
[26:51] Yeah, that's that's very exciting. And
[26:52] and how do you think about how Replit
[26:54] should evolve as the nature of the
[26:55] software uh development role evolves,
[26:58] right? From writing code to more
[27:00] managing agents, and then maybe managing
[27:02] agents that are managing other
[27:03] sub-agents.
[27:04] Um how are you thinking about that at at
[27:05] Replit?
[27:06] Well, really at this stage we we
[27:08] launched uh agent for back in like in
[27:10] early March this year.
[27:12] And what we try to do is like rethink
[27:16] what is the driver seat of the product.
[27:18] You don't you're not there anymore just
[27:20] thinking as a builder. We actually
[27:22] empower you to become a project manager.
[27:24] So, you spend quite a lot of time
[27:26] planning, and then we spin up agents in
[27:28] the background that accomplish the job.
[27:30] So, even though the product hides most
[27:33] of the complexity, in reality our users
[27:35] as of today they're managers of agents
[27:37] already. And I think that's the key
[27:40] of entrance for non-technical users.
[27:43] Like they they need to be able to
[27:45] unleash the power of several agents
[27:47] running in parallel, but they don't want
[27:49] to have the cognitive burden of having
[27:50] to manage tens or hundreds of them. It
[27:52] is is way too much. You even see
[27:54] developers running several shells in
[27:56] parallel, several IDEs. That is
[27:58] something that I think will be left only
[27:59] for the top coders in the world, and you
[28:02] know, everyone else will be just running
[28:03] agents in the background without even
[28:05] realizing that. Yeah.
[28:07] So, to dive a little bit deeper I guess
[28:09] on on how Replit um layers on the the
[28:13] underlying LLM or LLMs as you choose
[28:15] them, and and and provides dramatically
[28:17] more value to to to these engineers.
[28:21] Yeah. I
[28:23] I I think it's very important to put
[28:26] focus on the application layer on top of
[28:29] models. It's very exciting the race that
[28:31] we're experiencing in technology today.
[28:33] I think it's unprecedented how fast the
[28:34] frontier is moving.
[28:36] But at the same time that is that is raw
[28:38] power. And you if you put raw power in
[28:40] the hands of a power user, they come up,
[28:43] you know, they tailor their own
[28:44] workflow, they find different products
[28:46] that make their life easier.
[28:48] Uh but when you're targeting a knowledge
[28:49] worker who just wants to get job done,
[28:52] uh what they're looking for is a
[28:53] platform that allows you to get anything
[28:56] that you have in mind accomplished. So,
[28:59] over time we're starting to think even
[29:01] less about like a software-centric view
[29:04] for Replit. It's more a place where you
[29:05] can accomplish a knowledge worker task.
[29:08] And if that is by means of creating an
[29:10] application, by all means. Uh we are
[29:12] still creating agents, we are creating
[29:15] slide decks, we're creating motion
[29:17] videos. Like ultimately, we want to
[29:19] empower you to be creative by means of
[29:22] you you know, creating software in
[29:23] background, uh but the the entire
[29:26] complexity of writing code, maintaining
[29:28] code, publishing applications, that is
[29:30] completely hidden. And I think what I
[29:34] I'd love to talk about in public because
[29:35] I don't think many users understand
[29:37] that, is the same level of quality you
[29:39] get from a Google Cloud deployment when
[29:41] you when you build anything on Replit.
[29:43] So, we're using Cloud Run, and we're
[29:45] using Google Cloud Storage. So, even
[29:47] though you're building a MVP or like
[29:49] your prototype, you're really able to
[29:51] scale to millions of users overnight.
[29:53] That's amazing. So, so you you all get
[29:56] the the authorship to to replicate the
[29:58] the the maintenance and the devops you
[30:00] also take care for your customers. What
[30:02] the the software development life like
[30:04] what is building software look like in
[30:06] the future?
[30:07] I don't think we're going to
[30:10] and any more focus on the you know the
[30:12] SDLC, you know, the software development
[30:14] life cycle. Um and and the reason being
[30:17] if you if you spend some time on Rapid,
[30:19] the cycle is literally two hops. Like
[30:21] you you create the software and then you
[30:23] republish it. Everything else is
[30:25] happening in the background. We have a
[30:27] we have a prod agent, so it's a it's an
[30:30] agent that
[30:31] basically pulls all the time your
[30:33] deployments, checks if your queries are
[30:35] fast enough, checks if there are errors
[30:37] in your logs, and then recommends you
[30:39] how to fix the application.
[30:40] Uh we just launching today a way to keep
[30:44] track of all your software dependencies
[30:46] in terms of security. So, if there are
[30:47] vulnerabilities just published through a
[30:49] CVE, we immediately warn you about it
[30:52] and you know, we give you a task that
[30:54] the agent can accomplish. The only thing
[30:55] you need to do is to accept it and then
[30:57] republish the application. So, the the
[30:59] cycle that you know, we studied in
[31:00] college, you know, several different
[31:02] steps, you you know, you write your
[31:04] requirements and then you create the
[31:05] software and write tests, I think it's
[31:07] all being compressed into this creation
[31:10] experience that you have on Rapid. And
[31:12] then when you're ready to go, you
[31:14] publish the next version of the app.
[31:15] That's amazing.
[31:17] Can you talk a little bit more about how
[31:18] you're orchestrating the different
[31:19] models you mentioned? You you you pick
[31:21] the right size model sort of for the
[31:22] right size job. You know, what goes into
[31:24] that and then also is there are you
[31:26] using any open models at all in terms of
[31:28] your of your mix?
[31:31] When we put our hands first on the early
[31:34] versions of Flash and Flash Light, a lot
[31:37] of the testing was based on vibes. So,
[31:39] you know, we we literally tried to
[31:41] understand like how far can we push
[31:42] these models and we've been constantly
[31:44] impressed by how powerful they were. If
[31:47] you think about it, a Flash model is
[31:49] usually 6 months behind where the
[31:52] frontier was. So, if you use Flash
[31:54] today, you're basically using a way more
[31:56] expensive model from 6 months ago in the
[31:58] frontier.
[31:59] That really forces you to rethink your
[32:02] product roadmap. There will be things
[32:04] today that maybe are not affordable, you
[32:06] know, especially for like our long tail
[32:08] of users across the world,
[32:09] but 6 months from now we're going to be
[32:11] able to put them in the hands of
[32:12] everyone. So, that that is why I'm very
[32:15] excited about, you know, the the smaller
[32:16] models.
[32:17] The the way we we test them today is,
[32:20] you know, far more rigorous than you
[32:22] know, when just Flash came out. We have
[32:24] a lot of online evaluations. So,
[32:26] every single day there are several
[32:28] versions of our agent in front of
[32:29] everyone. We do a lot of AB testing as
[32:32] you can imagine and you know, we we try
[32:34] to understand which are the tasks, which
[32:36] are the, you know, accessory functions
[32:39] that the agent can accomplish with lower
[32:40] level of intelligence. And you we keep
[32:42] tuning that and it's a constant cycle
[32:44] basically on the on a nightly basis we
[32:46] redeploy a new agent for everyone.
[32:48] Got it. You used the word AB test and
[32:50] I'm very curious cuz cuz you have to
[32:51] support basically any app that could be
[32:54] written. How do you do evals and end
[32:56] tests and which part do you own and
[32:58] which part do you delegate to your your
[33:00] your users to to evaluate for
[33:02] themselves?
[33:04] So, we definitely put a lot of effort in
[33:06] creating offline evaluations and then we
[33:07] also collaborate with the Gemini team.
[33:09] We we give them insights of better
[33:12] models running on our evals. At the same
[33:14] time, a lot of effort goes on online
[33:17] evals and you're absolutely right. A lot
[33:20] of the applications are the first time
[33:23] we actually see them being built on
[33:24] Rapid. They're very similar to the, you
[33:26] know, queries on Google Search, you
[33:27] know, back in the days I think half of
[33:28] them are unique queries that hit the
[33:30] platform. It's similar for us.
[33:33] We can't really evaluate if a certain
[33:36] app has been built successfully, but
[33:37] what we can do is we can track the user
[33:40] sentiment, we can track if they keep
[33:41] engaging with a certain application and
[33:44] you we've been seeing the success rate
[33:46] and the sentiment of the user like
[33:48] climbing steadily over the last year and
[33:50] a half. Partially thanks to models
[33:52] becoming more powerful, but also thanks
[33:55] to the fact that our platform is far
[33:56] more exhaustive, that our agent harness
[33:58] is way more advanced than it was before
[34:00] and I I expect almost these numbers to
[34:03] start to plateau maybe by next year. Cuz
[34:05] the truth is
[34:06] a lot of the software that is
[34:08] created on Rapid or on byte coding
[34:10] platforms is not going to be as advanced
[34:12] as
[34:14] you know, maybe a database built on
[34:15] Google Cloud. You know, it's it's
[34:17] relatively simple software, you know,
[34:19] there are playbooks that can be
[34:20] followed, models are becoming extremely
[34:22] good at them and I'm very excited, you
[34:24] know, about literally the next 12 months
[34:26] cuz we're probably going to see a peak
[34:28] in terms of what byte coders can
[34:29] accomplish. Very exciting.
[34:33] I think we're
[34:34] Are we out of time? I have so many more
[34:36] questions.
[34:37] Do you want one more? Um well, I am
[34:39] wondering I guess for for everybody
[34:41] building things. You you mentioned that
[34:42] about half of the applications are are
[34:44] unique snowflakes. What are the common
[34:46] patterns that people are building either
[34:48] for their enterprises or as as, you
[34:51] know, weekend weekend warriors?
[34:54] I think the day one task that everyone
[34:56] does on our platform is they create a
[34:58] prototype of a an idea that they have in
[35:00] their minds. Uh they go to they do the
[35:02] zero to one really quickly. What makes
[35:04] Rapid different is that they're not just
[35:06] creating a front end or a landing page,
[35:08] they can actually go in depth and create
[35:09] a functional prototype.
[35:12] I think there are two paths from there.
[35:13] One is you show up at a meeting, you
[35:15] know, with decision makers and
[35:17] stakeholders. Suddenly you're not there
[35:20] with a slide deck or like a six-page
[35:22] essay. You you share your URL, they
[35:25] check the prototype and then they they
[35:27] test your product idea and they give a
[35:29] thumb up or thumb down.
[35:31] The second one that we're also very
[35:32] excited and I think it's really changing
[35:34] the the scenario of how SaaS is going to
[35:36] evolve over time is the fact that a lot
[35:38] of internal tools today are built on
[35:40] Rapid.
[35:41] >> Yeah. Of course we are big the fooders
[35:43] of our own product, but I can tell you
[35:45] if if you show up on Rapid today, maybe
[35:47] 80% of the software that they use on a
[35:49] daily basis is built on Rapid. So, all
[35:51] our internal tools ranging from HR,
[35:54] sales, and finance, everything is built
[35:56] on our platform and we see this
[35:58] happening across the entire industry.
[35:59] That's cool.
[36:01] Thank you for being a customer of our
[36:03] Gemini models and providing really
[36:05] valuable feedback. It's been super
[36:06] helpful for our teams.
[36:08] Thank you.
[36:12] >> [music]
