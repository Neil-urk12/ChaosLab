<h1>Safe use</h1>

<p>
	ChaosLab sends real requests to a real API and deliberately makes some of them fail. Use it only
	where that is safe and allowed.
</p>

<h2>Only targets you may test</h2>
<ul>
	<li>
		Test only APIs you own or are explicitly authorized to test. ChaosLab asks you to confirm this
		for each target.
	</li>
	<li>
		Use a development, test or staging environment, or a <strong>production mirror</strong>: an
		isolated copy of production, never production itself.
	</li>
	<li>Never point ChaosLab at production, or at a third party's infrastructure.</li>
</ul>

<h2>Requests change data</h2>
<ul>
	<li>
		Scenarios on <code>POST</code>, <code>PUT</code>, <code>PATCH</code> and <code>DELETE</code>
		operations create, change or remove records. Duplicate-request scenarios send the same request
		twice on purpose. ChaosLab warns you before such runs, and you can exclude any operation.
	</li>
	<li>Use test data and non-production credentials only.</li>
	<li>
		Runs execute one scenario at a time by default. You can raise this to five per run, which puts
		more load on the target.
	</li>
</ul>

<h2>Keep the instance private</h2>
<ul>
	<li>
		ChaosLab stores credentials for your target APIs. The pod binds its database and backend to
		<code>127.0.0.1</code>; keep it that way unless you serve it over HTTPS.
	</li>
	<li>
		The dependency simulator's address reaches your dependency through ChaosLab. Share it only with
		the target it is configured for.
	</li>
</ul>

<h2>What ChaosLab is not</h2>
<p>
	ChaosLab tests how an API handles failure. It is not a security scanner and does not look for
	vulnerabilities. Its findings describe what was observed, not a guarantee or certification.
</p>
