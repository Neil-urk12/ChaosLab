<script lang="ts">
	import './layout.css';
	import '@fontsource-variable/inter';
	import { asset, resolve } from '$app/paths';
	import { page } from '$app/state';
	import { repoUrl } from '$lib/site';

	let { children } = $props();

	const inDocs = $derived(page.url.pathname.startsWith(resolve('/docs')));
</script>

<svelte:head>
	<link rel="icon" href={asset('/favicon.svg')} />
</svelte:head>

<a
	href="#main"
	class="sr-only focus:not-sr-only focus:fixed focus:top-3 focus:left-3 focus:z-50 focus:rounded-md focus:bg-accent focus:px-3 focus:py-2 focus:text-[13px] focus:text-on-accent"
	>Skip to content</a
>

<header class="sticky top-0 z-40 border-b border-line-faint bg-canvas/85 backdrop-blur">
	<div class="mx-auto flex h-14 max-w-6xl items-center justify-between px-4 sm:px-6">
		<a href={resolve('/')} class="flex items-center gap-2 text-[15px] font-semibold text-strong">
			<img src={asset('/favicon.svg')} alt="" class="size-6" />
			ChaosLab
		</a>
		<nav aria-label="Main" class="flex items-center gap-1 text-[13px]">
			<a
				href={resolve('/docs/installation')}
				aria-current={inDocs ? 'page' : undefined}
				class={[
					'rounded-md px-3 py-1.5',
					inDocs ? 'bg-accent-tint text-slate-100' : 'text-slate-400 hover:text-slate-100'
				]}>Docs</a
			>
			<a href={repoUrl} class="rounded-md px-3 py-1.5 text-slate-400 hover:text-slate-100">GitHub</a>
		</nav>
	</div>
</header>

<main id="main">
	{@render children()}
</main>

<footer class="border-t border-line-faint">
	<div
		class="mx-auto flex max-w-6xl flex-col gap-3 px-4 py-8 text-[12px] text-slate-500 sm:flex-row sm:items-center sm:justify-between sm:px-6"
	>
		<p>ChaosLab Community. Open source under the AGPL-3.0 license.</p>
		<nav aria-label="Footer" class="flex gap-4">
			<a href={resolve('/docs/installation')} class="hover:text-slate-200">Docs</a>
			<a href={resolve('/docs/safe-use')} class="hover:text-slate-200">Safe use</a>
			<a href={`${repoUrl}/blob/develop/LICENSE`} class="hover:text-slate-200">License</a>
			<a href={repoUrl} class="hover:text-slate-200">GitHub</a>
		</nav>
	</div>
</footer>
