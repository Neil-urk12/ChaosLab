<script lang="ts">
	let { code, label = 'Copy commands' }: { code: string; label?: string } = $props();

	let copied = $state(false);

	async function copy() {
		try {
			await navigator.clipboard.writeText(code);
			copied = true;
			setTimeout(() => (copied = false), 2000);
		} catch {
			// Clipboard blocked (insecure context or permission); the text stays selectable.
		}
	}
</script>

<div class="not-prose relative rounded-lg border border-line bg-field">
	<button
		type="button"
		onclick={copy}
		aria-label={label}
		class="absolute top-2 right-2 rounded-md border border-line px-2 py-1 text-[11px] text-slate-400 hover:border-line-strong hover:text-slate-200 focus-visible:outline-2 focus-visible:outline-accent"
	>
		{copied ? 'Copied' : 'Copy'}
	</button>
	<pre class="overflow-x-auto p-4 pr-16 font-mono text-[12.5px] leading-6 text-slate-200"><code>{code}</code></pre>
	<span class="sr-only" aria-live="polite">{copied ? 'Copied to clipboard' : ''}</span>
</div>
