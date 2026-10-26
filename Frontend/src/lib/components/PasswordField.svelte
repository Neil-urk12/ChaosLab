<script lang="ts">
	let {
		id,
		label,
		placeholder,
		autocomplete,
		value = $bindable('')
	}: {
		id: string;
		label: string;
		placeholder: string;
		autocomplete: 'current-password' | 'new-password';
		value?: string;
	} = $props();

	let visible = $state(false);
</script>

<label for={id} class="mb-2 block text-xs font-medium text-slate-200">{label}</label>
<div class="relative">
	<input
		{id}
		type={visible ? 'text' : 'password'}
		{placeholder}
		{autocomplete}
		required
		bind:value
		class="auth-input pr-11"
	/>
	<button
		type="button"
		onclick={() => (visible = !visible)}
		aria-label={visible ? 'Hide password' : 'Show password'}
		aria-pressed={visible}
		class="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-r-md text-slate-500 hover:text-slate-300 focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-[#7b7dfb]"
	>
		<svg viewBox="0 0 24 24" class="size-4" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true">
			<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z" />
			<circle cx="12" cy="12" r="3" />
			{#if visible}<path d="M4 4l16 16" />{/if}
		</svg>
	</button>
</div>
