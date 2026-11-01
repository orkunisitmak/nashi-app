import importlib.util
import pathlib
import unittest

spec = importlib.util.spec_from_file_location("owner_merge", pathlib.Path(__file__).with_name("owner-auto-merge.py"))
merge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(merge)
SHA = "a" * 40


def pull():
    return {"number": 1, "state": "open", "draft": False, "user": {"id": merge.OWNER_ID},
            "head": {"sha": SHA, "repo": {"full_name": merge.REPOSITORY}},
            "base": {"ref": "main", "repo": {"full_name": merge.REPOSITORY}}}


def checks():
    return [{"name": n, "head_sha": SHA, "app": {"id": merge.ACTIONS_APP_ID},
             "status": "completed", "conclusion": "success"} for n in merge.REQUIRED_CHECKS]


class OwnerMergeTests(unittest.TestCase):
    def test_owner_only_and_ready_same_repository(self):
        self.assertTrue(merge.eligible(pull(), SHA))
        for key, value in [("draft", True), ("state", "closed"), ("user", {"id": 1}),
                           ("base", {"ref": "other"}), ("head", {"sha": SHA, "repo": None}),
                           ("head", {"sha": SHA, "repo": {"full_name": "someone/fork"}})]:
            pr = pull();pr[key] = value
            self.assertFalse(merge.eligible(pr, SHA), key)
        self.assertFalse(merge.eligible(pull(), "b" * 40))
        self.assertFalse(merge.eligible(pull(), "invalid"))

    def test_failed_pending_missing_foreign_and_stale_checks_block(self):
        self.assertTrue(merge.checks_passed(checks(), SHA))
        self.assertFalse(merge.checks_passed(checks()[:-1], SHA))
        for key, value in [("conclusion", "failure"), ("status", "in_progress"),
                           ("app", {"id": 1}), ("head_sha", "b" * 40)]:
            values = checks();values[0][key] = value
            self.assertFalse(merge.checks_passed(values, SHA), key)

    def test_success_elsewhere_never_masks_failed_or_pending_run_of_same_name(self):
        self.assertTrue(merge.checks_passed(checks() + checks(), SHA))
        for key, value in [("conclusion", "failure"), ("conclusion", "cancelled"),
                           ("status", "in_progress")]:
            extra = checks()[0];extra[key] = value
            self.assertFalse(merge.checks_passed(checks() + [extra], SHA), value)
        foreign = checks()[0];foreign["app"] = {"id": 1};foreign["conclusion"] = "failure"
        self.assertTrue(merge.checks_passed(checks() + [foreign], SHA))

    def test_merge_sha_guard_and_deployment_dispatch(self):
        class API:
            def __init__(self):self.calls = []
            def request(self, path, method="GET", data=None):
                self.calls.append((path, method, data))
                if "/check-runs?" in path:return {"check_runs": checks()}
                if "?state=open" in path:return [pull()]
                if path.endswith("/pulls/1"):return pull()
                if path.endswith("/merge"):return {"merged": True, "sha": "b" * 40}
        api = API();merge.merge_ready(api, SHA)
        writes = [(p, d) for p, method, d in api.calls if method != "GET"]
        self.assertEqual(len(writes), 3)
        self.assertEqual(writes[0][1], {"sha": SHA, "merge_method": "merge"})
        self.assertEqual(writes[1][1]["event_type"], "owner-pr-merged")
        self.assertTrue(writes[2][0].endswith("/pages/builds"))

    def test_foreign_owner_never_writes(self):
        class API:
            def request(self, path, method="GET", data=None):
                self_test.assertEqual(method, "GET")
                if "/check-runs?" in path:return {"check_runs": checks()}
                pr = pull();pr["user"]["id"] = 1
                return [pr]
        self_test = self;merge.merge_ready(API(), SHA)

    def test_new_commit_between_checks_and_merge_never_writes(self):
        class API:
            def request(self, path, method="GET", data=None):
                self_test.assertEqual(method, "GET")
                if "/check-runs?" in path:return {"check_runs": checks()}
                if "?state=open" in path:return [pull()]
                pr = pull();pr["head"]["sha"] = "b" * 40
                return pr
        self_test = self;merge.merge_ready(API(), SHA)


if __name__ == "__main__":
    unittest.main()
