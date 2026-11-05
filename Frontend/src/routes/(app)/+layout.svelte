<script lang="ts">
  import { goto } from "$app/navigation";
  import { page } from "$app/state";

  let { data, children } = $props();

  const initials = $derived(
    data.me.displayName
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((word) => word[0].toUpperCase())
      .join(""),
  );

  const sections = [
    {
      title: "Workspace",
      links: [
        {
          href: "/",
          label: "Overview",
          icon: '<rect x="4" y="4" width="6" height="6" rx="1.5"/><rect x="14" y="4" width="6" height="6" rx="1.5"/><rect x="4" y="14" width="6" height="6" rx="1.5"/><rect x="14" y="14" width="6" height="6" rx="1.5"/>',
        },
        {
          href: "/projects",
          label: "Projects",
          icon: '<path d="M8 8l-4 4 4 4M16 8l4 4-4 4M13.5 5l-3 14"/>',
        },
        {
          href: "/runs",
          label: "Test Runs",
          icon: '<path d="M7 5v14l11-7z"/>',
        },
        {
          href: "/findings",
          label: "Findings",
          icon: '<path d="M12 4l9 16H3zM12 10v4M12 17h.01"/>',
        },
      ],
    },
    {
      title: "Manage",
      links: [
        {
          href: "/team",
          label: "Team",
          icon: '<circle cx="9" cy="8" r="3.5"/><path d="M3 20c0-3.5 2.7-6 6-6s6 2.5 6 6M16 4.5a3.5 3.5 0 010 7M21 20c0-3-1.5-5-4-5.7"/>',
        },
        {
          href: "/settings",
          label: "Settings",
          icon: '<path d="M4 7h10M18 7h2M4 17h4M12 17h8"/><circle cx="16" cy="7" r="2"/><circle cx="10" cy="17" r="2"/>',
        },
      ],
    },
  ];

  const current = $derived(
    sections.flatMap((s) => s.links).find((l) => l.href === page.url.pathname)
      ?.label,
  );

  async function logout() {
    await fetch("/api/auth/logout", { method: "POST" });
    await goto("/login");
  }
</script>

<div
  class="flex min-h-screen bg-[#07090e] font-['Inter_Variable',system-ui,sans-serif] text-slate-100"
>
  <aside
    class="sticky top-0 hidden h-screen w-60 shrink-0 flex-col border-r border-[#1a1f2b] px-3 py-6 lg:flex"
  >
    <a href="/" class="px-3 text-lg font-bold tracking-tight">Chaos Lab</a>
    <nav aria-label="Main" class="mt-10 space-y-8">
      {#each sections as section (section.title)}
        <div>
          <p
            class="px-3 text-[10px] font-semibold tracking-wider text-slate-500 uppercase"
          >
            {section.title}
          </p>
          <ul class="mt-3 space-y-1">
            {#each section.links as link (link.href)}
              {@const active = link.href === page.url.pathname}
              <li>
                <a
                  href={link.href}
                  aria-current={active ? "page" : undefined}
                  class={[
                    "flex items-center gap-3 rounded-md px-3 py-2 text-[13px]",
                    active
                      ? "bg-[#1b1d3a] text-slate-100"
                      : "text-slate-400 hover:bg-[#0d111a] hover:text-slate-200",
                  ]}
                >
                  <svg
                    viewBox="0 0 24 24"
                    class="size-4"
                    fill="none"
                    stroke="currentColor"
                    stroke-width="1.6"
                    stroke-linejoin="round"
                    stroke-linecap="round"
                    aria-hidden="true">{@html link.icon}</svg
                  >
                  {link.label}
                </a>
              </li>
            {/each}
          </ul>
        </div>
      {/each}
    </nav>

    <div
      class="mt-auto flex items-center gap-3 rounded-lg border border-[#1e2433] px-3 py-3"
    >
      <span
        class="grid size-8 shrink-0 place-items-center rounded-full bg-[#1b1d3a] text-[11px] font-semibold text-[#a5a6fd]"
        aria-hidden="true">{initials}</span
      >
      <div class="min-w-0">
        <p class="truncate text-[13px]">{data.me.displayName}</p>
        <p class="truncate text-[11px] text-slate-500">
          {data.me.roles[0] ?? data.me.userName}
        </p>
      </div>
    </div>
  </aside>

  <div class="flex min-w-0 flex-1 flex-col">
    <header
      class="flex h-16 items-center justify-between border-b border-[#1a1f2b] px-6 lg:px-8"
    >
      <p class="text-[13px] text-slate-500">
        Workspace <span aria-hidden="true">/</span>
        <span class="text-slate-200">{current}</span>
      </p>
      <div class="flex items-center gap-4">
        <button
          type="button"
          onclick={logout}
          class="rounded-md p-1.5 text-slate-400 hover:text-slate-100"
          aria-label="Sign out"
        >
          <svg
            viewBox="0 0 24 24"
            class="size-5"
            fill="none"
            stroke="currentColor"
            stroke-width="1.6"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <path
              d="M14 4H6a1 1 0 00-1 1v14a1 1 0 001 1h8M10 12h10M17 9l3 3-3 3"
            />
          </svg>
        </button>
        <span
          class="grid size-8 place-items-center rounded-full bg-[#1b1d3a] text-[11px] font-semibold text-[#a5a6fd]"
          title={data.me.displayName}>{initials}</span
        >
      </div>
    </header>

    <main class="mx-auto w-full max-w-6xl px-4 py-10 lg:px-8">
      {@render children()}
    </main>
  </div>
</div>
