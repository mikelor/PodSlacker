# An inside look at the evolution of agent development

**Source:** https://youtu.be/vS_SaCohHgs?si=EQt_xdUjfj2l9GhW  
**Video ID:** `vS_SaCohHgs`

---

## Agentic Development: The Future of Data Platforms

This video discusses the shift in data platform architecture from human-centric designs to agent-centric ones, driven by the evolving capabilities of AI agents. The speaker, Yasmine Ahmed from Google Data Cloud, explains how traditional data platforms, built for human interaction speeds and rigid application interfaces, are becoming obsolete. The future lies in agentic data clouds designed for reasoning efficiency, with agents as the primary users.

### Key Topics Covered:

*   **Evolution of Data Consumers:** The shift from humans and their applications as primary data consumers to AI agents.
*   **Limitations of Human-Centric Architectures:**
    *   Data platforms were built for human speed (e.g., dashboards, UIs) which slows down technology.
    *   Rigid APIs and applications create tech debt and hinder flexibility.
*   **The Rise of Agents:**
    *   Agents are the future of data interaction, with their own reasoning loops and higher interaction volumes.
    *   They bypass traditional UIs and interact directly with APIs.
    *   Emergence of "agents as a service" rather than "SaaS."
*   **Rethinking the IT Stack for Agents:**
    *   **Reasoning Engine:** Moving beyond retrieval (SQL) to true reasoning capabilities, requiring a unified interface, data, and compute.
    *   **Interface Layer:** Agents speak code and prefer agentic terminals and skills over GUIs.
    *   **Data Layer:** The need for a unified engine supporting vector embeddings, graph databases, relational data, and unstructured data processing, moving beyond solely SQL.
    *   **Infrastructure:** The necessity for powerful AI hardware (like Google's AI Hypercomputer) designed for rapid, multi-step reasoning.
*   **Orchestration and Intent-Driven Engineering:**
    *   The shift from imperative, task-based coding to defining outcomes and letting AI determine the path.
    *   The concept of "swarms of agents" activated to achieve a defined intent.
    *   "Agentic harness" as the framework that makes powerful AI models usable for real-world scenarios, comprising:
        *   **Identity:** Defining guardrails, enterprise memory, and data access.
        *   **Capabilities:** Providing the skills, tools, and workflows for agents to act.
*   **Examples of Agentic Applications:**
    *   Manhattan Associates: Autonomous rebooking of freight based on real-time weather and traffic.
    *   MakeMyTrip: Building a multimodal, voice-activated itinerary planner using a unified data engine.
    *   Supply Chain Mitigation: A swarm of agents responding to shipping disruptions in seconds.
    *   decentralized Finance (DeFi) Trading: Agents performing risk audits, route planning, and real-time trade verification.

### Key Takeaways:

*   Data platforms are fundamentally changing from being built for human interaction to being built for AI agent interaction.
*   Agents drive significantly higher volumes of interaction and require different architectural considerations than humans.
*   The future of data platforms lies in "agentic data clouds" optimized for reasoning efficiency.
*   This involves a unified interface, a comprehensive data engine capable of various data types and queries, and specialized AI infrastructure.
*   Orchestration is shifting from manual instructions to intent-driven workflows powered by swarms of agents acting in concert.
*   The "agentic harness" is crucial for translating powerful AI models into practical, real-world applications.

### Notable Quotes:

*   "Architectures are collapsing because the user that we are architecting around no longer has a pulse. Agents are the future."
*   "Agents don't query, they reason."
*   "We're moving from a world of SaaS to actually agents as a service."
*   "The agentic harness is the same [as the web browser for the internet]. The agentic harness creates that environment that makes these super powerful models easy to apply to real world scenarios."
*   "The era of instruction is over. As we look at the agentic world today, we now hear about intent-driven engineering. Define the outcome and let AI figure out the path."

---

## Podcast Script

**MIKE:** Welcome everyone, thanks for joining us. Today, we're diving deep into something that's reshaping the tech landscape: agentic development.

**JORDAN:** Yeah, it's a fascinating shift, isn't it? We've heard so much about AI lately, but this is about the *next* evolution – thinking about how these agents will fundamentally change how we interact with data and technology.

**MIKE:** Exactly. The core idea is that the data industry has historically been built for human users and the applications they employ. We've spent decades creating interfaces – dashboards, UIs, buttons – to make data accessible to humans.

**JORDAN:** Because, let's face it, humans aren't naturally fluent in data. We need things translated, slowed down, made digestible, which really caters to human speed, not machine speed.

**MIKE:** And then there are the applications we build, which rely on rigid APIs. As soon as those APIs are established, changing them becomes tech debt because any application built on them can break. So, we've had this very human-centric, deterministic system.

**JORDAN:** But that's precisely what's changing. The speaker highlights that architectures are collapsing because the primary user is no longer a human with a pulse. Instead, "agents are the future."

**MIKE:** This is a massive paradigm shift. In product strategy sessions, agent is now the central persona, not data engineers or business users. The entire interaction layer is being handed over to agents.

**JORDAN:** And agents don't operate like humans at all. We're already seeing massive spikes in web traffic, not because humans are clicking faster, but because agents are hitting APIs.

**MIKE:** It's a crucial distinction. A human might click once, but an agent's reasoning loop means it can query, test, and hit an API numerous times in the same period, leading to exponential increases in compute.

**JORDAN:** So, we're moving beyond simple retrieval systems like SQL. Agents *reason*, they don't just query, which implies a much more complex way of interacting with data.

**MIKE:** This is evident in how agents are operating *over* existing applications. While they might not fully replace SaaS applications, they're certainly demoting the application layer by bypassing interfaces and going direct to APIs.

**JORDAN:** The example of Manhattan Associates running supply chains really drives this home. They have specialized agents that autonomously rebook freight based on real-time weather and traffic, or reroute forklifts for inventory.

**MIKE:** These aren't just summarizing; they're *acting*. And trying to run an "agentic enterprise" on a data stack built for humans simply doesn't work; it breaks the system.

**JORDAN:** That's why Google is rethinking its IT stack, inverting it across three layers: reasoning engine, interface, and data. It all starts with the interface.

**MIKE:** The speaker notes a fascinating resurgence of CLIs and code, which makes sense because agents "speak code." They're not necessarily interested in pretty UIs.

**JORDAN:** They can drop into a sandbox, write and test Python code, and even write patches for broken APIs – tasks that used to take humans days or weeks to resolve.

**MIKE:** Beneath the interface is the engine. The traditional SQL engine, while still relevant, is being augmented. Agents operate on intent, which requires more than just SQL.

**JORDAN:** Intent necessitates capabilities like vector embedding search, graph processing, and unstructured data handling. The MakeMyTrip example shows how fragmented tools hinder creating interactive experiences.

**MIKE:** So, the journey is towards a unified engine with zero hops, integrating these diverse capabilities. This is crucial for speed and for building these new agent-centric experiences.

**JORDAN:** And to power this speed, you need incredible silicon. Agents generate vast amounts of "thinking tokens" in their reasoning loops, far exceeding human processing speeds.

**MIKE:** If chips are designed for human speed, they're too slow. This is why Google is focusing on its AI hypercomputer, separating training and inferencing, and compressing memory.

**JORDAN:** This leads us to a fundamental shift: from a data cloud built for storage efficiency to an "agentic data cloud" built for *reasoning efficiency*. It's an entirely different mindset.

**MIKE:** And with all that speed, how do you drive outcomes? That's where orchestration comes in. The era of explicit instruction or imperative code is over.

**JORDAN:** We're moving towards "intent-driven engineering" – defining the outcome and letting AI figure out the path, often involving "swarms of agents" working together.

**MIKE:** The Strait of Hormuz example is striking. A traditional human sprint to deal with a supply chain disruption took 72 hours, with stale data by the end.

**JORDAN:** Now, an intent like "preserve margins" can trigger a swarm that researches shelf life, fetches pricing, checks compliance, and outputs a migration strategy in *under three seconds*.

**MIKE:** But how does that swarm activate? It's not just the model, like Gemini. You need a "harness" to turn that powerful model into real-world outcomes.

**JORDAN:** This harness is like the web browser for the internet. It provides identity, guardrails, enterprise memory, and a capability layer of skills and tools that allow agents to take action.

**MIKE:** For the decentralized finance platform Infinite, this harness allows a swarm to detect liquidity risks, audit smart contracts, draft routing paths, and verify trades in milliseconds, compressing a 20-minute process.

**JORDAN:** It's a monumental leap from human-driven, slow, deterministic systems to agent-driven, fast, intent-based operations. The implications for every industry are profound.

**MIKE:** Absolutely. We've moved from building for human speed to enabling agent reasoning, which requires a complete reimagining of our data infrastructure and how we orchestrate complex workflows.

**JORDAN:** So, the key takeaways are the shift to agents as the central persona, the move from retrieval to reasoning, the need for unified engines and specialized infrastructure, and the importance of the "agentic harness" for orchestrating action.

**MIKE:** It's a new frontier, and the next six months are going to be incredibly exciting as these concepts continue to mature and be implemented.

**JORDAN:** Indeed. Thanks for joining us on this deep dive into agentic development. We'll catch you next time!

