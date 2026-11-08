<script lang="ts">
	let {
		checked,
		label,
		disabled = false,
		onchange
	}: { checked: boolean; label: string; disabled?: boolean; onchange?: (checked: boolean) => void } = $props();
</script>

<button
	type="button"
	role="switch"
	aria-checked={checked}
	aria-label={label}
	{disabled}
	onclick={(event) => {
		event.stopPropagation();
		onchange?.(!checked);
	}}
	class="relative inline-flex h-5 w-9 shrink-0 items-center rounded-full transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent disabled:cursor-not-allowed disabled:opacity-50 {checked ? 'bg-accent' : 'bg-line-strong'}"
>
	<span class="inline-block size-3.5 rounded-full bg-white transition-transform {checked ? 'translate-x-[18px]' : 'translate-x-[3px]'}"></span>
</button>
