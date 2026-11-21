<script lang="ts">
	import { resolve } from '$app/paths';
	import CodeBlock from '$lib/CodeBlock.svelte';
	import { fromSourceSetup, fromSourceStart, repoUrl } from '$lib/site';

	const pipeline = [
		['Import', 'Paste, upload or fetch an OpenAPI 3.x spec.'],
		['Discover', 'Find the endpoints, parameters, schemas and auth requirements.'],
		['Generate', 'Plan the chaos scenarios that apply to each endpoint.'],
		['Execute', 'Send real requests to your target API, with a baseline first and the fault applied.'],
		['Observe', 'Record status, timing, bodies, retries and recovery as evidence.'],
		['Evaluate', 'Judge each scenario Pass, Degraded, Fail, Inconclusive or Not Applicable.'],
		['Report', 'Get a Resilience Score, recommendations and a PDF report.']
	];

	const primitives = [
		['Latency', 'Delays a response, to check timeouts and slow-dependency handling.'],
		['5xx Storm', 'Answers with repeated 500, 502 or 503 errors, to check retries and backoff.'],
		['429 Throttle', 'Answers 429 Too Many Requests with Retry-After, to check rate-limit handling.'],
		['Timeout / Drop', 'Holds or drops the connection, to check timeout enforcement and recovery.'],
		['Malformed JSON', 'Corrupts the response body, to check parser and schema error handling.'],
		['Empty Response', 'Returns an empty body where a payload is expected.'],
		['Slow Drip', 'Delivers the body a few bytes at a time, to check whole-operation deadlines.'],
		['Authentication Expiry', 'Sends missing or invalid credentials, to check 401 and 403 handling.'],
		['DNS / Dependency-Resolution Failure', "Cuts the target's calls to a dependency, to check fallback and graceful degradation."],
		['Duplicate request', 'Sends the same state-changing request twice, to check idempotency.']
	];

	// Scoring version 1.0 (PROJECT.md 14.2): stored by the backend, shown here only as documentation.
	const categories = [
		['Timeout', 20],
		['Retry', 15],
		['Graceful Degradation', 15],
		['Rate-Limit', 10],
		['Idempotency', 10],
		['Authentication Failure', 10],
		['Response Validation', 10],
		['Dependency Failure', 10]
	] as const;
</script>

<svelte:head>
	<title>ChaosLab: API resilience testing</title>
	<meta
		name="description"
		content="Open-source, self-hosted resilience testing for REST APIs. ChaosLab runs controlled failure scenarios from your OpenAPI spec and scores how your API copes."
	/>
</svelte:head>

<section class="mx-auto max-w-6xl px-4 pt-16 pb-12 sm:px-6 sm:pt-24">
	<p class="text-[12px] font-semibold tracking-wider text-accent-ink uppercase">
		Open source · Self-hosted
	</p>
	<h1 class="mt-4 max-w-3xl text-4xl font-semibold tracking-tight text-strong sm:text-5xl">
		See how your API behaves when things go wrong.
	</h1>
	<p class="mt-5 max-w-2xl text-[15px] leading-relaxed text-slate-400">
		ChaosLab reads your OpenAPI spec, runs controlled failure scenarios against your running API in a
		non-production environment, and turns what it observes into findings, a 0–100 Resilience Score
		and recommendations.
	</p>

	<div class="mt-10 max-w-3xl">
		<p
			class="mb-3 inline-flex rounded-md border border-line bg-panel px-3 py-1.5 text-[12px] text-slate-300"
		>
			One-command install arrives with v0.1.0. Until then, install from source.
		</p>
		<CodeBlock code={fromSourceSetup} label="Copy the setup commands" />
		<p class="my-3 text-[13px] text-slate-300">
			Set the same database password and credentials key in <code>secret.yaml</code> and
			<code>Backend/.env.local</code>, then build and start:
		</p>
		<CodeBlock code={fromSourceStart} label="Copy the build and start commands" />
		<p class="mt-3 text-[12px] text-slate-500">
			Needs Podman, <code>envsubst</code>, Make, the .NET 10 SDK with <code>dotnet-ef</code>, Node.js,
			pnpm and <code>openssl</code>.
		</p>
		<div class="mt-5 flex flex-wrap gap-3 text-[13px]">
			<a
				href={resolve('/docs/installation')}
				class="rounded-md bg-accent px-4 py-2 font-semibold text-on-accent hover:bg-accent-hover focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent"
				>Installation guide</a
			>
			<a
				href={repoUrl}
				class="rounded-md border border-line px-4 py-2 text-slate-200 hover:border-line-strong"
				>View on GitHub</a
			>
		</div>

		<aside
			aria-labelledby="warning-title"
			class="mt-8 rounded-lg border border-warn-line bg-warn-panel p-4 text-[13px] leading-relaxed"
		>
			<h2 id="warning-title" class="font-semibold text-amber-200">Non-production targets only</h2>
			<p class="mt-1 text-amber-300">
				ChaosLab sends real requests, including repeated state-changing ones. Point it only at APIs you
				own or are authorized to test, in a development, test, staging or isolated production-mirror
				environment. Never at production.
				<a href={resolve('/docs/safe-use')} class="underline underline-offset-2">Safe use</a>
			</p>
		</aside>
	</div>
</section>

<section aria-labelledby="pipeline-title" class="border-t border-line-faint">
	<div class="mx-auto max-w-6xl px-4 py-16 sm:px-6">
		<h2 id="pipeline-title" class="text-2xl font-semibold text-strong">From spec to score</h2>
		<p class="mt-2 max-w-2xl text-[14px] text-slate-400">
			One pipeline, run for you. ChaosLab never tests a spec on its own: it needs your API running.
		</p>
		<ol class="mt-8 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
			{#each pipeline as [step, text], i (step)}
				<li class="rounded-lg border border-line bg-panel p-4">
					<p class="text-[11px] font-semibold text-accent-ink">{i + 1}</p>
					<h3 class="mt-1 text-[14px] font-semibold text-slate-100">{step}</h3>
					<p class="mt-1 text-[13px] leading-relaxed text-slate-400">{text}</p>
				</li>
			{/each}
		</ol>
	</div>
</section>

<section aria-labelledby="primitives-title" class="border-t border-line-faint">
	<div class="mx-auto max-w-6xl px-4 py-16 sm:px-6">
		<h2 id="primitives-title" class="text-2xl font-semibold text-strong">What it breaks</h2>
		<p class="mt-2 max-w-2xl text-[14px] text-slate-400">
			Nine chaos primitives and a duplicate-request scenario, each applied only where it fits the
			endpoint.
		</p>
		<dl class="mt-8 grid gap-x-8 gap-y-5 sm:grid-cols-2">
			{#each primitives as [name, text] (name)}
				<div class="border-l-2 border-accent-tint pl-4">
					<dt class="text-[14px] font-semibold text-slate-100">{name}</dt>
					<dd class="mt-1 text-[13px] leading-relaxed text-slate-400">{text}</dd>
				</div>
			{/each}
		</dl>
	</div>
</section>

<section aria-labelledby="results-title" class="border-t border-line-faint">
	<div class="mx-auto max-w-6xl px-4 py-16 sm:px-6">
		<h2 id="results-title" class="text-2xl font-semibold text-strong">What you get</h2>
		<!-- shortcut: text only until screenshots of a run against a non-production target exist; add them here. -->
		<div class="mt-8 grid gap-4 lg:grid-cols-3">
			<div class="rounded-lg border border-line bg-panel p-5 lg:row-span-2">
				<h3 class="text-[14px] font-semibold text-slate-100">An explainable Resilience Score</h3>
				<p class="mt-1 text-[13px] leading-relaxed text-slate-400">
					0 to 100, the weighted mean of eight categories. Inconclusive results are left out and shown
					as evidence coverage instead.
				</p>
				<ul class="mt-4 space-y-2 text-[13px]">
					{#each categories as [name, weight] (name)}
						<li class="flex items-center justify-between gap-3">
							<span class="text-slate-300">{name}</span>
							<span class="text-slate-500 tabular-nums">{weight}%</span>
						</li>
					{/each}
				</ul>
			</div>
			<div class="rounded-lg border border-line bg-panel p-5">
				<h3 class="text-[14px] font-semibold text-slate-100">Findings with evidence</h3>
				<p class="mt-1 text-[13px] leading-relaxed text-slate-400">
					Each finding states what was expected, what was observed and what it does not prove, with
					the requests and responses behind it.
				</p>
			</div>
			<div class="rounded-lg border border-line bg-panel p-5">
				<h3 class="text-[14px] font-semibold text-slate-100">Recommendations</h3>
				<p class="mt-1 text-[13px] leading-relaxed text-slate-400">
					Every Fail and Degraded finding comes with a rule-based fix, such as bounded retries with
					backoff or idempotency keys. ChaosLab never changes your code.
				</p>
			</div>
			<div class="rounded-lg border border-line bg-panel p-5 lg:col-span-2">
				<h3 class="text-[14px] font-semibold text-slate-100">Run history and PDF reports</h3>
				<p class="mt-1 text-[13px] leading-relaxed text-slate-400">
					Follow the score from run to run, and export a PDF report of any finished run with sensitive
					values redacted.
				</p>
			</div>
		</div>
	</div>
</section>
