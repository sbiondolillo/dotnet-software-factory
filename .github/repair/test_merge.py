"""Unit tests of the path rule and the base rule of merge.py, decided in #406 and its addendum of 2026-11-01. The
build job of ci.yml runs them with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. They use
unittest, which the standard library holds, so the job installs nothing. The tests of the keys read factory.yml of
the branch. A FakeRepo of .github/shared/ stands in for the repo, so no test calls GitHub. A 404 on the content of a
file and the delete of a branch that exists are facts of repo.py, and test_repo.py tests them."""

import os
import signal
import unittest
from unittest import mock

import merge
import runs
import traveler
import verdict
from fake_github import closes
from fake_repo import Fail, FakeRepo
from github import GitHubError

with open(
    os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "workflows", "factory.yml"), encoding="utf-8"
) as file:
    FACTORY_TEXT = file.read()


class GuardedTests(unittest.TestCase):
    def test_each_listed_file_is_refused(self):
        for path in (
            ".github/workflows/judge.yml",
            ".github/workflows/ci.yml",
            ".github/workflows/repair.yml",
            ".claude/skills/change/JUDGES.md",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [path])

    def test_a_path_under_a_listed_directory_is_refused(self):
        for path in (
            ".github/judge/judge.py",
            ".github/repair/merge.py",
            ".github/repair/new.py",
            ".github/shared/github.py",
            ".github/skills/code-review/SKILL.md",
            ".github/skills/new/SKILL.md",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [path])

    def test_every_other_workflow_path_is_refused(self):
        for path in (
            ".github/workflows/x.yml",
            ".github/workflows/relock.yml",
            ".github/workflows/ci.yml.bak",
            ".github/workflows/sub/x.yml",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [path])

    def test_an_open_path_passes(self):
        for path in (
            ".github/workflows/factory.yml",
            "factory.yml",
            "src/x.cs",
            ".github/agents/builder.md",
            "sandbox/Dockerfile",
            ".github/judgement/x.py",
            ".github/skillset/x.md",
            ".claude/skills/change/SKILL.md",
            "docs/.github/repair/merge.py",
        ):
            with self.subTest(path=path):
                self.assertEqual(merge.guarded([path]), [])

    def test_the_refusal_names_every_listed_path_once(self):
        paths = ["src/x.cs", ".github/repair/merge.py", ".github/workflows/judge.yml", ".github/repair/merge.py"]
        self.assertEqual(merge.guarded(paths), [".github/repair/merge.py", ".github/workflows/judge.yml"])


class FactoryKeyTests(unittest.TestCase):
    def edit(self, old, new):
        self.assertEqual(FACTORY_TEXT.count(old), 1)
        return FACTORY_TEXT.replace(old, new)

    def test_the_file_on_the_branch_holds_both_keys(self):
        self.assertEqual(len(merge.key_blocks(FACTORY_TEXT, "on", True)), 1)
        self.assertEqual(merge.key_blocks(FACTORY_TEXT, "permissions", False), [["permissions:", "  checks: read"]])

    def test_an_edit_outside_the_keys_passes(self):
        head = self.edit("# The run name is", "# The name of the run is").replace(
            "    runs-on: ubuntu-latest", "    runs-on: ubuntu-24.04"
        )
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_a_plain_step_edit_passes(self):
        head = self.edit("./builder.md", "./builder-x.md")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_a_step_edit_with_an_expression_passes(self):
        head = self.edit("    runs-on: ubuntu-latest\n", "    runs-on: ubuntu-latest\n    timeout-minutes: ${{ 90 }}\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_an_edit_of_a_comment_in_a_key_passes(self):
        head = self.edit("on:\n  issues:\n", "on:\n  # the label path\n  issues:\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_an_added_comment_that_names_the_key_passes(self):
        head = self.edit("  factory:\n", "  factory:\n    # permissions: write\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_a_quote_inside_a_plain_scalar_passes(self):
        head = self.edit("Read the builder's ids", "Read the builder's two ids").replace(
            "  factory:\n", "  factory:\n    # a comment that isn't a key\n"
        )
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [])

    def test_a_changed_line_that_opens_a_quoted_scalar_is_refused(self):
        for line in ("x: 'x''\n", 'x: "closed"\n', "x: 'closed'\n", "- 'item'\n", '"key": value\n'):
            with self.subTest(line=line):
                head = self.edit("permissions:\n  checks: read\n", line + "permissions:\n  checks: read\n")
                self.assertIn(merge.UNREADABLE, merge.changed_keys(FACTORY_TEXT, head))

    def test_a_line_with_a_byte_order_mark_or_a_homoglyph_is_refused(self):
        for new in ("\ufeffon:\n  pull_request:\n", "p\u0435rmissions:\n  checks: write\n"):
            with self.subTest(new=new):
                head = new + FACTORY_TEXT
                self.assertIn(merge.UNREADABLE, merge.changed_keys(FACTORY_TEXT, head))

    def test_the_file_on_the_branch_holds_no_character_outside_ascii_in_a_line_the_rule_reads(self):
        lines = [line for line in FACTORY_TEXT.splitlines() if not line.lstrip().startswith("#")]
        self.assertEqual([line for line in lines if merge.NON_ASCII.search(line)], [])

    def test_an_added_pull_request_trigger_is_refused(self):
        head = self.edit("on:\n  issues:\n", "on:\n  pull_request:\n  issues:\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["on:"])

    def test_a_trigger_after_a_comment_at_column_0_is_refused(self):
        head = self.edit("on:\n  issues:\n", "on:\n# x\n  pull_request:\n  issues:\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["on:"])

    def test_a_job_level_grant_is_refused(self):
        head = self.edit("  factory:\n", "  factory:\n    permissions:\n      checks: write\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:"])

    def test_a_grant_in_a_flow_mapping_is_refused(self):
        head = FACTORY_TEXT + "  forge: {runs-on: ubuntu-latest, permissions: {checks: write}, steps: []}\n"
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:", merge.UNREADABLE])

    def test_a_flow_mapping_with_no_key_name_is_refused(self):
        head = self.edit("    runs-on: ubuntu-latest\n", "    runs-on: ubuntu-latest\n    env: { checks: write }\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [merge.UNREADABLE])

    def test_a_flow_key_split_over_two_lines_is_refused(self):
        head = (
            FACTORY_TEXT + '  forge: {"runs-on": "ubuntu-latest", "steps": [],\n    "permissions"\n    : "write-all"}\n'
        )
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:", merge.UNREADABLE])

    def test_a_split_key_alone_is_refused(self):
        head = self.edit(
            "    runs-on: ubuntu-latest\n", '    runs-on: ubuntu-latest\n    "permissions"\n    : "write-all"\n'
        )
        self.assertIn("permissions:", merge.changed_keys(FACTORY_TEXT, head))

    def test_the_key_name_in_a_scalar_is_refused(self):
        for step in ("      - run: 'echo permissions: read'\n", '      - name: "Show permissions: read"\n'):
            with self.subTest(step=step):
                head = self.edit("    steps:\n", "    steps:\n" + step)
                self.assertIn("permissions:", merge.changed_keys(FACTORY_TEXT, head))

    def test_the_key_name_in_a_comment_beside_a_quote_is_refused(self):
        head = self.edit("    steps:\n", "    steps:\n      - run: echo 'x' # permissions: write\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:"])

    def test_a_grant_in_another_spelling_is_refused(self):
        for line in (
            "    permissions\t: write-all\n",
            '    "permissions":write-all\n',
            "    Permissions: write-all\n",
            "    'PERMISSIONS': write-all\n",
        ):
            with self.subTest(line=line):
                head = self.edit("  factory:\n", "  factory:\n" + line)
                self.assertIn("permissions:", merge.changed_keys(FACTORY_TEXT, head))

    def test_a_grant_after_a_tag_or_an_anchor_is_refused(self):
        for line in ("    !!str permissions: write-all\n", "    &grant permissions: write-all\n"):
            with self.subTest(line=line):
                head = self.edit("  factory:\n", "  factory:\n" + line)
                self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:", merge.UNREADABLE])

    def test_a_trigger_in_another_spelling_is_refused(self):
        for line in ('"on":[pull_request]\n', "on\t: pull_request\n", "!!str on: pull_request\n"):
            with self.subTest(line=line):
                self.assertIn("on:", merge.changed_keys(FACTORY_TEXT, FACTORY_TEXT + line))

    def test_a_key_in_a_hidden_form_is_refused(self):
        for line in (
            '    "perm\\x69ssions": write-all\n',
            "    ? perm\n    : write-all\n",
            "    *grant : write-all\n",
            "    <<: *grant\n",
            "    env: &grant x\n",
            "    if: !cancelled\n",
        ):
            with self.subTest(line=line):
                head = self.edit("  factory:\n", "  factory:\n" + line)
                self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), [merge.UNREADABLE])

    def test_the_file_on_the_branch_holds_no_hidden_form(self):
        hidden = rf"""{merge.NODE}(?:[!&*]|\?(?:[ \t]|$))|"[^"]*\\"""
        for line in FACTORY_TEXT.splitlines():
            with self.subTest(line=line):
                self.assertNotRegex(line, hidden)

    def test_each_flow_on_the_branch_closes_on_its_line(self):
        for line in FACTORY_TEXT.splitlines():
            if merge.UNREADABLE in merge.line_causes(line):
                with self.subTest(line=line):
                    self.assertEqual(line.count("[") + line.count("{"), line.count("]") + line.count("}"))

    def test_a_changed_top_level_grant_is_refused(self):
        head = self.edit("  checks: read\n", "  checks: write\n")
        self.assertEqual(merge.changed_keys(FACTORY_TEXT, head), ["permissions:"])

    def test_a_deleted_file_is_refused(self):
        for base, head in ((FACTORY_TEXT, None), (None, FACTORY_TEXT)):
            with self.subTest(deleted=head is None):
                self.assertEqual(merge.changed_keys(base, head), ["on:", "permissions:", merge.UNREADABLE])

    def test_the_refusal_names_the_changed_key(self):
        self.assertEqual(
            merge.refused([".github/workflows/x.yml"], ["on:"]),
            ["`.github/workflows/x.yml`", "the `on:` key of `.github/workflows/factory.yml`"],
        )
        self.assertEqual(
            merge.refused([], ["on:", "permissions:"]),
            ["the `on:` and `permissions:` keys of `.github/workflows/factory.yml`"],
        )
        self.assertEqual(
            merge.refused([], [merge.UNREADABLE]),
            ["a line in a form the job cannot read in `.github/workflows/factory.yml`"],
        )
        self.assertEqual(merge.refused([], []), [])


REPO = "o/r"
SHA = "a" * 40
BASE_SHA = "b" * 40
MERGE_SHA = "m" * 40
# A `createdAt` far in the future, so it is after the `since` of each dispatch.
NOW = "2100-01-01T00:00:00Z"
CLOSE = f"Merged as `{MERGE_SHA}` by pull request #7, merged by `github-actions`."
# The latest judge-verdict on the head, as Repo.latest_check gives it: absent, in progress and passed.
ABSENT = None
RUNNING = ("in_progress", None)
PASSED = ("completed", "success")


def count(repo, method, **arguments):
    """The number of calls of the method whose arguments hold each of `arguments`."""
    return sum(all(call[key] == value for key, value in arguments.items()) for call in repo.calls_of(method))


class ChangedFilesTests(unittest.TestCase):
    def test_a_rename_gives_both_paths(self):
        repo = FakeRepo(REPO)
        repo.answer(
            "files",
            (7,),
            [
                {"filename": "src/moved.py", "previous_filename": ".github/repair/merge.py"},
                {"filename": ".github/workflows/factory.yml"},
            ],
        )
        count, paths = merge.changed_files(repo, 7)
        self.assertEqual(count, 2)
        self.assertEqual(paths, ["src/moved.py", ".github/repair/merge.py", ".github/workflows/factory.yml"])
        self.assertEqual(merge.guarded(paths), [".github/repair/merge.py"])


class BaseTests(unittest.TestCase):
    """main() over a FakeRepo that answers every read and write of the step."""

    def setUp(self):
        self.addCleanup(signal.signal, signal.SIGTERM, signal.getsignal(signal.SIGTERM))

    def pull_request(self, base, **fields):
        """The pull request on `base`. GitHub links no issue to a pull request whose base is not the default branch,
        so only a pull request on main closes issue 5."""
        return {
            "number": 7,
            "state": "OPEN",
            "labels": [{"name": "repair"}],
            "headRefName": "repair/5",
            "headRefOid": SHA,
            "baseRefName": base,
            "baseRefOid": BASE_SHA,
            **(closes(REPO, 5) if base == "main" else closes(REPO)),
            "changedFiles": 1,
            **fields,
        }

    def traveler_body(self, **fields):
        ticket = traveler.Traveler(
            run=f"https://github.com/{REPO}/actions/runs/9", target="`repair/9`", sha=SHA, session="sesn_1", **fields
        )
        return f"The fix.\n\n{traveler.render(ticket)}"

    def run_main(
        self,
        *bases,
        paths=("src/x.cs",),
        issue=None,
        red_branch="main",
        gate=("c" * 40,),
        env=None,
        verdicts=None,
        repo=None,
        head_text=FACTORY_TEXT,
    ):
        """The fake after main() ran. Each read of the pull request gives the next base, and the read after the arm
        gives it merged. `gate` is the head SHA of the gate run, then each status of it in turn, and its
        conclusion. `verdicts` are the states of judge-verdict in turn, a completed success by default. `head_text`
        answers the read of factory.yml at the head. A test that expects main() to raise hands in its own `repo`."""
        repo = repo if repo is not None else FakeRepo(REPO)
        merged = self.pull_request("main", state="MERGED", mergeCommit={"oid": MERGE_SHA})
        merged["mergedBy"] = {"login": "github-actions"}
        repo.answer("pull_request", (7,), *[self.pull_request(base) for base in bases], merged)
        repo.answer("default_branch", (), "main")
        repo.answer("issue", (5,), {**(issue or {"body": "", "labels": []}), "state": "OPEN"})
        repo.answer("files", (7,), [{"filename": path} for path in paths])
        repo.answer("file_at", (merge.FACTORY, BASE_SHA), FACTORY_TEXT)
        repo.answer("file_at", (merge.FACTORY, SHA), head_text)
        repo.answer("workflow_run", (9,), {"head_branch": red_branch})
        repo.answer("latest_check", (verdict.CHECK, SHA), *(verdicts or [PASSED]))
        self.take_comments(repo)
        gate_run = {"databaseId": 1, "headSha": gate[0], "displayTitle": "#451", "url": "u", "createdAt": NOW}
        repo.answer("workflow_runs", (merge.WORKFLOW, "repair/5", None, "workflow_dispatch"), [], [gate_run])
        repo.answer("dispatch_workflow", (merge.WORKFLOW, "repair/5", {"issue": 451, "base": "main"}), None)
        repo.answer(
            "run_status", (1,), *[{"status": status, "conclusion": conclusion} for status, conclusion in gate[1:]]
        )
        repo.answer("enable_auto_merge", (7, SHA), None)
        repo.answer("dispatch_workflow", ("ci.yml", "main"), None)
        repo.answer("close_issue", (5, CLOSE), None)
        repo.answer("delete_branch_if_present", ("repair/5",), False)
        env = {"REPAIR_ENABLED": "true", "GITHUB_REPOSITORY": REPO, "REPAIR_POLL_SECONDS": "0", **(env or {})}
        with (
            mock.patch.dict(os.environ, env),
            mock.patch.object(merge.sys, "argv", ["merge.py", "7", SHA]),
            mock.patch.object(runs.time, "sleep"),
        ):
            merge.main(repo)
        return repo

    @staticmethod
    def take_comments(repo):
        """Issue 5 and pull request 7 take the comments of comments.py, and each list holds each one posted there."""
        for number in (5, 7):
            repo.answer(
                "comments",
                (number,),
                lambda number=number: [{"body": text} for text in BaseTests.comments(repo, number)],
            )
            repo.answer("post_comment", (number, mock.ANY), None)

    @staticmethod
    def comments(repo, number=5):
        """The body of each comment posted on issue or pull request <number>."""
        return [call["body"] for call in repo.calls_of("post_comment") if call["number"] == number]

    def test_a_second_stop_on_the_head_posts_no_second_comment(self):
        repo = FakeRepo(REPO)
        self.take_comments(repo)
        for _ in range(2):
            merge.stop(repo, 7, 5, SHA, "No merge.")
        [text] = self.comments(repo)
        self.assertIn(f"repair-merge:{SHA}", text.splitlines()[0])
        self.assertEqual(count(repo, "comments", number=5), 2)

    def test_a_pull_request_on_main_arms_the_merge_and_closes_the_issue(self):
        repo = self.run_main("main", "main", "main")
        self.assertEqual(self.comments(repo), [])
        self.assertEqual(repo.calls_of("enable_auto_merge"), [{"number": 7, "sha": SHA}])
        self.assertEqual(repo.calls_of("close_issue"), [{"number": 5, "comment": CLOSE}])
        self.assertEqual(count(repo, "dispatch_workflow", workflow="ci.yml", ref="main"), 1)
        self.assertEqual(repo.calls_of("delete_branch_if_present"), [{"branch": "repair/5"}])

    def test_a_pull_request_on_another_base_is_refused_with_one_comment_on_the_pull_request(self):
        repo = self.run_main("dev")
        self.assertEqual(self.comments(repo), [])
        [text] = self.comments(repo, 7)
        self.assertIn("Pull request #7 targets `dev`, not `main`.", text)
        self.assertEqual(count(repo, "files"), 0)
        self.assertEqual(count(repo, "enable_auto_merge"), 0)

    def test_a_retarget_while_the_job_reads_the_files_merges_nothing(self):
        repo = self.run_main("main", "dev")
        self.assertEqual(count(repo, "files"), 1)
        self.assertEqual(self.comments(repo), [])
        self.assertEqual(count(repo, "latest_check"), 0)
        self.assertEqual(count(repo, "enable_auto_merge"), 0)

    def test_a_retarget_before_the_arm_merges_nothing(self):
        repo = self.run_main("main", "main", "dev")
        self.assertEqual(count(repo, "latest_check"), 1)
        self.assertEqual(self.comments(repo), [])
        self.assertEqual(count(repo, "enable_auto_merge"), 0)

    def test_a_factory_diff_with_no_key_change_arms_the_merge(self):
        repo = self.run_main("main", "main", "main", paths=[merge.FACTORY])
        self.assertEqual(self.comments(repo), [])
        self.assertEqual((count(repo, "file_at", sha=BASE_SHA), count(repo, "file_at", sha=SHA)), (1, 1))
        self.assertEqual(count(repo, "enable_auto_merge"), 1)

    def test_a_failed_read_of_factory_yml_ends_the_job_and_merges_nothing(self):
        repo = FakeRepo(REPO)
        with self.assertRaises(GitHubError):
            self.run_main("main", "main", "main", paths=[merge.FACTORY], repo=repo, head_text=Fail(status=502))
        self.assertEqual(count(repo, "file_at", sha=SHA), 1)
        self.assertEqual(self.comments(repo), [])
        self.assertEqual(count(repo, "enable_auto_merge"), 0)

    def test_a_guarded_path_is_refused_with_one_comment_before_the_judge(self):
        repo = self.run_main("main", "main", paths=[".github/workflows/judge.yml"])
        [text] = self.comments(repo)
        self.assertIn("`.github/workflows/judge.yml`", text)
        self.assertEqual(count(repo, "latest_check"), 0)
        self.assertEqual(count(repo, "enable_auto_merge"), 0)

    def test_the_branch_of_the_red_run_in_the_traveler_is_refused_with_one_comment(self):
        repo = self.run_main("main", "main", issue={"body": self.traveler_body(), "labels": []}, red_branch="repair/5")
        [text] = self.comments(repo)
        self.assertIn("is the branch of the red run", text)
        self.assertEqual(count(repo, "latest_check"), 0)

    def factory_repair(self, *bases, gate, env=None):
        issue = {"body": self.traveler_body(story=451), "labels": [{"name": "factory"}]}
        return self.run_main(*bases, issue=issue, gate=gate, env=env)

    def test_a_factory_repair_dispatches_the_gate_on_the_story_of_its_traveler(self):
        repo = self.factory_repair("main", "main", gate=("c" * 40,))
        self.assertEqual(self.comments(repo), [])
        self.assertEqual(count(repo, "dispatch_workflow", workflow=merge.WORKFLOW, ref="repair/5"), 1)
        self.assertEqual(count(repo, "run_status"), 0)
        self.assertEqual(count(repo, "enable_auto_merge"), 0)

    def test_a_green_gate_run_on_the_settled_head_arms_the_merge(self):
        repo = self.factory_repair("main", "main", "main", gate=(SHA, ("queued", ""), ("completed", "success")))
        self.assertEqual(self.comments(repo), [])
        [gate] = [call for call in repo.calls_of("dispatch_workflow") if call["workflow"] == merge.WORKFLOW]
        self.assertEqual(gate["ref"], "repair/5")
        self.assertEqual(list(gate["inputs"].items()), [("issue", 451), ("base", "main")])
        self.assertEqual(count(repo, "run_status", run_id=1), 2)
        self.assertEqual(count(repo, "enable_auto_merge"), 1)
        self.assertEqual(count(repo, "dispatch_workflow", workflow="ci.yml", ref="main"), 1)
        self.assertEqual(repo.calls_of("close_issue"), [{"number": 5, "comment": CLOSE}])

    def test_a_red_gate_run_is_one_comment_and_no_merge(self):
        repo = self.factory_repair("main", "main", gate=(SHA, ("completed", "failure")))
        [text] = self.comments(repo)
        self.assertIn("The gate run u of the story #451", text)
        self.assertIn("is `failure`", text)
        self.assertEqual(count(repo, "enable_auto_merge"), 0)

    def test_a_gate_run_in_progress_at_the_timeout_exits_and_arms_no_merge(self):
        with self.assertRaises(SystemExit) as stop:
            self.factory_repair(
                "main", "main", gate=(SHA, ("in_progress", "")), env={"REPAIR_GATE_TIMEOUT_SECONDS": "0"}
            )
        self.assertIn("the gate run", stop.exception.code)
        self.assertIn("after the timeout", stop.exception.code)

    def test_a_factory_repair_whose_traveler_names_no_story_is_refused_with_one_comment(self):
        issue = {"body": self.traveler_body(pull_request=3), "labels": [{"name": "factory"}]}
        repo = self.run_main("main", "main", issue=issue)
        [text] = self.comments(repo)
        self.assertIn("the traveler of its issue names no story", text)
        self.assertEqual(count(repo, "latest_check"), 0)

    # The wait for judge-verdict, through runs.poll: an absent check and one in progress are waited on.

    def test_an_absent_then_running_check_is_waited_on_and_the_merge_arms(self):
        repo = self.run_main("main", "main", "main", verdicts=[ABSENT, RUNNING, PASSED])
        self.assertEqual(count(repo, "latest_check", name=verdict.CHECK, sha=SHA), 3)
        self.assertEqual(self.comments(repo), [])
        self.assertEqual(count(repo, "enable_auto_merge"), 1)

    def test_an_absent_check_at_the_timeout_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.run_main("main", "main", verdicts=[ABSENT], env={"REPAIR_MERGE_TIMEOUT_SECONDS": "0"})
        self.assertEqual(stop.exception.code, f"repair: {verdict.CHECK} on {SHA[:7]} is absent after the timeout")

    def test_a_running_check_at_the_timeout_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.run_main("main", "main", verdicts=[RUNNING], env={"REPAIR_MERGE_TIMEOUT_SECONDS": "0"})
        self.assertEqual(stop.exception.code, f"repair: {verdict.CHECK} on {SHA[:7]} is in_progress after the timeout")


class WaitForMergeTests(unittest.TestCase):
    def wait(self, base, head=SHA, disarm=None):
        repo = FakeRepo(REPO)
        repo.answer("pull_request", (7,), {"state": "OPEN", "headRefOid": head, "baseRefName": base})
        repo.answer("disable_auto_merge", (7,), disarm)
        return merge.wait_for_merge(repo, 7, SHA, "main", 0, 0), repo

    def test_a_retarget_while_armed_disarms(self):
        result, repo = self.wait("dev")
        self.assertIsNone(result)
        self.assertEqual(repo.calls_of("disable_auto_merge"), [{"number": 7}])

    def test_a_moved_head_while_armed_disarms(self):
        result, repo = self.wait("main", head="c" * 40)
        self.assertIsNone(result)
        self.assertEqual(repo.calls_of("disable_auto_merge"), [{"number": 7}])

    def test_a_failed_disarm_is_a_log_line(self):
        result, repo = self.wait("dev", disarm=Fail(status=502))
        self.assertIsNone(result)
        self.assertEqual(repo.calls_of("disable_auto_merge"), [{"number": 7}])

    def test_an_unmoved_pull_request_waits_to_the_timeout(self):
        with self.assertRaises(SystemExit):
            self.wait("main")


if __name__ == "__main__":
    unittest.main()
