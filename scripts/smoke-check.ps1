param(
	[string]$ApiBaseUrl = "http://localhost:5291",
	[string]$ApiKey = "admin123",
	[switch]$RunMutations
)

$ErrorActionPreference = "Stop"

function Normalize-ApiBaseUrl {
	param([string]$Url)
	if ([string]::IsNullOrWhiteSpace($Url)) { return "http://localhost:5291" }
	if ($Url.EndsWith("/")) { return $Url.TrimEnd('/') }
	return $Url
}

function Invoke-Api {
	param(
		[string]$Method,
		[string]$Path,
		[object]$Body = $null,
		[switch]$UseApiKey
	)

	$headers = @{}
	if ($UseApiKey) {
		$headers["X-API-Key"] = $ApiKey
	}

	$uri = "$script:baseUrl$Path"
	if ($null -ne $Body) {
		$json = $Body | ConvertTo-Json -Depth 8
		return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType "application/json" -Body $json -TimeoutSec 20
	}

	return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -TimeoutSec 20
}

$baseUrl = Normalize-ApiBaseUrl -Url $ApiBaseUrl
$failures = New-Object System.Collections.Generic.List[string]

Write-Host "== Smoke check started =="
Write-Host "API: $baseUrl"
Write-Host "RunMutations: $RunMutations"

# 1) Startup/health diagnostics
try {
	$health = Invoke-Api -Method "GET" -Path "/health/startup"
	if ($health.status -eq "ok") {
		Write-Host "[PASS] health/startup status=$($health.status), db.canConnect=$($health.database.canConnect)"
	} else {
		$failures.Add("Health status is '$($health.status)' (expected ok).")
		Write-Host "[FAIL] health/startup status=$($health.status)"
	}
}
catch {
	$failures.Add("Cannot call /health/startup: $($_.Exception.Message)")
	Write-Host "[FAIL] Cannot call /health/startup"
}

# 2) Pending registrations
$pending = @()
try {
	$pending = @(Invoke-Api -Method "GET" -Path "/api/poiregistration/pending")
	Write-Host "[PASS] pending registrations count=$($pending.Count)"
}
catch {
	$failures.Add("Cannot load pending registrations: $($_.Exception.Message)")
	Write-Host "[FAIL] pending registrations"
}

# 3) Approve/Reject endpoint availability (safe check with invalid id)
$approvalProbeBody = @{ notes = "smoke-check"; reviewedBy = 0 }
try {
	Invoke-Api -Method "POST" -Path "/api/poiregistration/-1/approve" -Body $approvalProbeBody | Out-Null
	Write-Host "[WARN] approve probe returned success on invalid id; endpoint reachable"
}
catch {
	$code = $_.Exception.Response.StatusCode.value__
	if ($code -in @(400, 404)) {
		Write-Host "[PASS] approve endpoint reachable (status $code on invalid id)"
	}
	else {
		$failures.Add("Approve endpoint probe failed with unexpected status $code")
		Write-Host "[FAIL] approve endpoint probe status=$code"
	}
}

try {
	Invoke-Api -Method "POST" -Path "/api/poiregistration/-1/reject" -Body $approvalProbeBody | Out-Null
	Write-Host "[WARN] reject probe returned success on invalid id; endpoint reachable"
}
catch {
	$code = $_.Exception.Response.StatusCode.value__
	if ($code -in @(400, 404)) {
		Write-Host "[PASS] reject endpoint reachable (status $code on invalid id)"
	}
	else {
		$failures.Add("Reject endpoint probe failed with unexpected status $code")
		Write-Host "[FAIL] reject endpoint probe status=$code"
	}
}

# 4) App load-all + pin source count
$pois = @()
try {
	$loadAll = Invoke-Api -Method "GET" -Path "/api/poi/load-all?lang=vi&includeUnpublished=true" -UseApiKey
	$pois = @($loadAll.items)
	if ($pois.Count -gt 0) {
		Write-Host "[PASS] load-all items count=$($pois.Count)"
	}
	else {
		$failures.Add("load-all returned zero items")
		Write-Host "[FAIL] load-all returned zero items"
	}
}
catch {
	$failures.Add("Cannot call /api/poi/load-all: $($_.Exception.Message)")
	Write-Host "[FAIL] load-all"
}

# 5) POI edit + content save (optional mutate, idempotent update)
if ($RunMutations -and $pois.Count -gt 0) {
	try {
		$poi = $pois[0].poi
		if ($null -eq $poi) { throw "No POI object from load-all payload" }

		# idempotent save with unchanged values
		Invoke-Api -Method "PUT" -Path "/api/poi/$($poi.id)" -Body $poi -UseApiKey | Out-Null
		Write-Host "[PASS] poi edit save for id=$($poi.id)"

		$contents = @(Invoke-Api -Method "GET" -Path "/api/content/by-poi/$($poi.id)")
		if ($contents.Count -gt 0) {
			$content = $contents[0]
			Invoke-Api -Method "PUT" -Path "/api/content/$($content.id)" -Body $content | Out-Null
			Write-Host "[PASS] content save for contentId=$($content.id), poiId=$($poi.id)"
		}
		else {
			Write-Host "[WARN] no content found for poiId=$($poi.id), skip content save"
		}
	}
	catch {
		$failures.Add("Mutating save checks failed: $($_.Exception.Message)")
		Write-Host "[FAIL] poi edit/content save"
	}
}
else {
	Write-Host "[INFO] Skip POI edit + content save. Use -RunMutations to execute idempotent save checks."
}

if ($failures.Count -gt 0) {
	Write-Host ""
	Write-Host "== Smoke check FAILED =="
	$failures | ForEach-Object { Write-Host " - $_" }
	exit 1
}

Write-Host ""
Write-Host "== Smoke check PASSED =="
exit 0
