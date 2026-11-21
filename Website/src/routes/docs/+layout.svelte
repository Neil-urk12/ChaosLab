<script lang="ts">
	import { resolve } from '$app/paths';
	import { page } from '$app/state';
	import { docsPages } from '$lib/site';

	let { children } = $props();

	const current = $derived(docsPages.find((p) => resolve(p.href) === page.url.pathname));
</script>

<svelte:head>
	<title>{current ? `${current.title} · ChaosLab docs` : 'ChaosLab docs'}</title>
</svelte:head>

<div class="mx-auto flex max-w-6xl flex-col gap-8 px-4 py-10 sm:px-6 md:flex-row md:gap-12">
	<nav aria-label="Docs" class="md:w-52 md:shrink-0">
		<p class="px-3 text-[10px] font-semibold tracking-wider text-slate-500 uppercase">Docs</p>
		<ul class="mt-3 flex flex-wrap gap-1 md:flex-col">
			{#each docsPages as p (p.href)}
				{@const active = p === current}
				<li>
					<a
						href={resolve(p.href)}
						aria-current={active ? 'page' : undefined}
						class={[
							'block rounded-md px-3 py-1.5 text-[13px]',
							active
								? 'bg-accent-tint text-slate-100'
								: 'text-slate-400 hover:bg-field hover:text-slate-200'
						]}>{p.title}</a
					>
				</li>
			{/each}
		</ul>
	</nav>
	<article class="docs min-w-0 flex-1">
		{@render children()}
	</article>
</div>
