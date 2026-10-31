#!/usr/bin/env python3
"""Merge only the owner's checked PRs. Executed from trusted main, never PR code."""
import json
import os
import re
import urllib.error
import urllib.request

REPOSITORY = "orkunisitmak/nashi-app"
OWNER_ID = 68860338
ACTIONS_APP_ID = 15368
REQUIRED_CHECKS = {"security", "mac-source", "windows"}


def eligible(pr, head):
    return (bool(re.fullmatch(r"[0-9a-f]{40}", head))
            and pr.get("state") == "open" and not pr.get("draft", True)
            and pr.get("user", {}).get("id") == OWNER_ID
            and pr.get("base", {}).get("ref") == "main"
            and pr.get("base", {}).get("repo", {}).get("full_name") == REPOSITORY
            and (pr.get("head", {}).get("repo") or {}).get("full_name") == REPOSITORY
            and pr.get("head", {}).get("sha") == head)


def checks_passed(checks, head):
    # Every Actions run of a required name on this head must succeed: another PR
    # from the same commit must not stand in for a failed or pending run.
    runs = [c for c in checks if c["name"] in REQUIRED_CHECKS and c.get("head_sha") == head
            and c.get("app", {}).get("id") == ACTIONS_APP_ID]
    return ({c["name"] for c in runs} == REQUIRED_CHECKS
            and all(c.get("status") == "completed" and c.get("conclusion") == "success" for c in runs))


class GitHub:
    def __init__(self, token):
        self.token = token

    def request(self, path, method="GET", data=None):
        if not path.startswith("/repos/" + REPOSITORY + "/"):
            raise ValueError("This automation is restricted to its own repository")
        headers = {"Authorization": "Bearer " + self.token,
                   "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
                   "User-Agent": "Nashi-owner-auto-merge"}
        if data is not None:
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request("https://api.github.com" + path, headers=headers,
                                         method=method, data=json.dumps(data).encode() if data is not None else None)
        # These fixed REST endpoints do not redirect. Never forward the job token.
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                return None
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=30) as response:
            raw = response.read()
            return json.loads(raw) if raw else None


def merge_ready(api, head):
    prefix = "/repos/" + REPOSITORY
    checks = api.request(prefix + "/commits/" + head + "/check-runs?filter=latest&per_page=100")["check_runs"]
    if not checks_passed(checks, head):
        print("Waiting for required security, Mac and Windows checks.")
        return
    pulls = api.request(prefix + "/pulls?state=open&base=main&per_page=100")
    for candidate in pulls:
        if not eligible(candidate, head):
            continue
        number = int(candidate["number"])
        # Re-read after checks, then use GitHub's SHA guard and branch protection.
        current = api.request(prefix + "/pulls/" + str(number))
        if not eligible(current, head):
            continue
        try:
            result = api.request(prefix + "/pulls/" + str(number) + "/merge", "PUT",
                                 {"sha": head, "merge_method": "merge"})
        except urllib.error.HTTPError as error:
            if error.code in (405, 409):
                print("PR changed, has conflicts, or is still blocked by branch protection.")
                continue
            raise RuntimeError("GitHub refused the merge: HTTP " + str(error.code)) from None
        if not result.get("merged"):
            raise RuntimeError("GitHub did not confirm a merge")
        print("Automatically merged owner PR #" + str(number) + " as " + result["sha"])
        # GITHUB_TOKEN merges do not emit normal push workflows. Explicit dispatch
        # preserves the existing post-merge CI; Pages' legacy build has its own API.
        api.request(prefix + "/dispatches", "POST", {"event_type": "owner-pr-merged",
                    "client_payload": {"pull_request": number, "merge_sha": result["sha"]}})
        api.request(prefix + "/pages/builds", "POST")
        print("Requested post-merge source/Windows checks and the Pages build.")


def main():
    if os.environ.get("GITHUB_REPOSITORY") != REPOSITORY:
        raise RuntimeError("Unexpected repository")
    with open(os.environ["GITHUB_EVENT_PATH"], encoding="utf-8") as source:
        event = json.load(source)
    if os.environ.get("GITHUB_EVENT_NAME") == "pull_request_target":
        pr = event.get("pull_request", {})
        head = pr.get("head", {}).get("sha", "")
        if event.get("action") != "ready_for_review" or not eligible(pr, head):
            return
    else:
        run = event.get("workflow_run", {})
        if run.get("event") != "pull_request" or run.get("conclusion") != "success":
            return
        head = run.get("head_sha", "")
    if not re.fullmatch(r"[0-9a-f]{40}", head):
        raise RuntimeError("Invalid workflow head")
    merge_ready(GitHub(os.environ["GH_TOKEN"]), head)


if __name__ == "__main__":
    main()
