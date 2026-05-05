# An inside look at the evolution of agent development

**Source:** https://youtu.be/vS_SaCohHgs?si=EQt_xdUjfj2l9GhW  
**Video ID:** `vS_SaCohHgs`

---

## Agentic Development: The Future of Data Platforms

This session explores the evolution of data platforms from human-centric interfaces to agent-centric architectures. The speaker, Yasmine Ahmed from Google Data Cloud, discusses how the industry is shifting towards agents as the primary users, driving a fundamental reimagining of the data stack. This includes changes in the interface, the underlying data engine, and the infrastructure powering these agents. The talk also delves into the concept of orchestration, moving from imperative, task-based instructions to intent-driven engineering powered by swarms of agents working together.

### Major Sections & Topics:

1.  **The Shift from Human to Agent Users:**
    *   Historically, data platforms were built for human users via UIs, dashboards, and rigid APIs, slowing down technology to human speed.
    *   Agents, however, operate at machine speed, leading to massive spikes in web traffic from API interactions.
    *   Agents don't just retrieve information; they reason, loop, test, and can hit APIs multiple times for a single "human click," significantly increasing compute demands.

2.  **The Inverted IT Stack for Agents:**
    *   The traditional IT stack needs to be inverted to support agents. This involves three key layers:
        *   **Reasoning Engine (Interface Layer):** Moving away from GUIs to "agentic terminals" and interfaces that agents understand (e.g., CLIs, code). Agents can write, test, and execute code to resolve issues or create tools.
        *   **Single Engine (Data Layer):** The SQL engine is being replaced or augmented. Agents operate on intent, requiring capabilities beyond traditional SQL, including vector embedding, graph processing, relational, and unstructured data handling. Examples like MakeMyTrip demonstrate the need for a unified engine.
        *   **Infrastructure Layer:** Silicon designed for human speed is insufficient for agentic reasoning loops. Google's AI hypercomputer, with innovations in separating training and inferencing, is crucial for agent performance.

3.  **Orchestration and Intent-Driven Engineering:**
    *   The era of imperative, task-based instructions is ending.
    *   The future is intent-driven engineering: defining the desired outcome and letting AI figure out the path.
    *   This is enabled by "swarms of agents" working collaboratively.
    *   An example of global supply chain disruption highlights how a 72-hour human sprint can be compressed into seconds with agent swarms achieving rapid mitigation strategies.

4.  **Agentic Harness for Action:**
    *   Powerful AI models (like Gemini) need a "harness" to translate them into real-world outcomes.
    *   This harness provides:
        *   **Identity:** Guardrails, enterprise memory, data access, and defined actions for agents.
        *   **Capabilities:** Skills, tools, workflows, and the ability to interact with the real world.
    *   The harness allows agents to orchestrate complex workflows through identity and capabilities, as demonstrated by a decentralized finance trading platform example.

### Key Takeaways:

*   The data industry is experiencing a paradigm shift from human-centric to agent-centric architectures.
*   Agents operate at machine speed and require a fundamentally different data stack, from interfaces to infrastructure.
*   The future of data platforms is an "agentic data cloud" optimized for reasoning efficiency, not just storage efficiency.
*   Intent-driven engineering and agent swarms are revolutionizing how complex outcomes are achieved.
*   An "agentic harness" is essential for making powerful AI models actionable and applying them to real-world scenarios.

### Notable Quotes:

*   "Architectures are collapsing because the user that we are architecting around no longer has a pulse. Agents are the future."
*   "Agents don't just click once... an agent might actually hit that API 10 to 20 times."
*   "We're no longer dealing with retrieval systems, the old SQL world. We are now dealing with reasoning because agents don't query, they reason."
*   "You can't build on a set of fragmented tools. You actually need the interface, the data, and the compute to work seamlessly together."
*   "CLIs are sexy again. Suddenly the code is very interesting again. Why? Because agents speak code."
*   "The era of instruction is over. As we look at the agentic world today, we now hear about intent-driven engineering."
*   "The agentic harness creates that environment that makes these super powerful models easy to apply to real world scenarios."

---

## Podcast Script

**MIKE:** Welcome everyone, so glad you could join us today! We're diving deep into the fascinating world of agentic development.

**JORDAN:** It's going to be a great conversation, Mike. We're not just talking about the tech itself, but really exploring how our thinking about these "agents" has evolved over the past year and where it's heading.

**MIKE:** Exactly, Jordan. The source material highlights a significant shift in how we've approached data and applications over the last decade. We've primarily built systems for human users and the applications they interact with.

**JORDAN:** And the core issue there, right? Humans aren't naturally data-fluent. So we created all these dashboards and UIs, essentially slowing down technology to meet human pace.

**MIKE:** It's a crucial point. We built interfaces to bridge that gap, even if it meant waiting for a human to grab a coffee while a query ran. Applications, too, were built on rigid APIs, which became tech debt waiting to happen.

**JORDAN:** But that's all changing dramatically. The source talks about architectures collapsing because the "user" we're designing for is no longer human. Agents are the new central persona.

**MIKE:** And this is where the impact is so profound. Agents don't operate at human speed, or with human limitations. Look at web traffic spikes – that’s agents, not humans clicking faster.

**JORDAN:** It's mind-blowing when you think about it. An agent’s reasoning loop means it can hit an API 10 to 20 times in the time a human makes one click. That's a massive increase in compute demand.

**MIKE:** Precisely. We're moving from retrieval-based systems, like the old SQL world, to systems focused on reasoning. Agents don't just query; they reason through problems.

**JORDAN:** This also means that our existing applications, while still relevant, are being bypassed. The source mentions agents going direct to APIs, demoting the application interface layer.

**MIKE:** That's the nuance to the "agents replacing SaaS" question. It's more about agents becoming the service, rather than directly replacing the applications themselves.

**JORDAN:** The Manhattan Associates example is perfect here. They're using specialized agents for things like real-time shipping rebooking based on weather and traffic. These agents are *acting*, not just summarizing.

**MIKE:** And that leads to the core challenge: running an "agentic enterprise" on a data stack built for humans just doesn't work. So, Google is rethinking the IT stack, inverting it into three layers.

**JORDAN:** The first layer they're focusing on is the interface. It's interesting how CLIs and code are becoming "sexy" again, because agents speak code and bypass traditional UIs.

**MIKE:** Yes, agents can write and test code in sandboxes, or even write patches for broken APIs. This shifts the focus from user-facing UIs to agentic terminals and the tools they need.

**JORDAN:** And then beneath that interface is the data engine. The source argues that while SQL engines will remain, they're not ideal for agents operating on *intent*.

**MIKE:** Intent requires more than just SQL. It needs vector embeddings, graph capabilities, unstructured data processing. The MakeMyTrip example shows how fragmented stacks struggle, driving the need for a unified engine.

**JORDAN:** That unified engine is key, especially with the infrastructure. Agents can generate thousands of "thinking tokens" per command, so chips designed for human speed are too slow.

**MIKE:** Exactly. Google's AI hypercomputer, separating training and inferencing, is crucial for handling this massive compute demand and removing traffic jams.

**JORDAN:** So we have a new interface, a unified engine, and powerful compute. This is shifting us from a data cloud built for storage efficiency to one built for *reasoning* efficiency.

**MIKE:** And the next logical step is orchestration. The era of instruction-based, imperative code is ending; we're moving to intent-driven engineering.

**JORDAN:** "Define the outcome and let AI figure out the path." That's the new paradigm, often involving "swarms" of agents working together.

**MIKE:** The Strait of Hormuz example powerfully illustrates this. A 72-hour human sprint to solve a global shipping crisis is now compressed into seconds by an agent swarm.

**JORDAN:** It's incredible. The source emphasizes that this isn't just about powerful models like Gemini, but about the "agentic harness" that makes them usable in the real world.

**MIKE:** That harness provides identity – guardrails, memory, access – and a capability layer – skills, tools, workflows. It's what allows agents to orchestrate complex outcomes.

**JORDAN:** The Infinite example in decentralized finance really hammers this home. A swarm of agents handles discovery, risk auditing, execution, and verification in milliseconds, down from 20 minutes.

**MIKE:** So, we've seen a massive evolution, from human-centric design to agent-centric architecture, driven by the need for reasoning, speed, and intent.

**JORDAN:** It’s a fundamental shift in how we interact with and leverage technology, and it's only just beginning. Thanks for joining us on this deep dive!

**MIKE:** Absolutely. We'll catch you next time. Goodbye!

