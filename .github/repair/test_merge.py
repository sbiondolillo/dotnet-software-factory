"""Unit tests of the path rule and the base rule of merge.py, decided in #406 and its addendum of 2026-11-01. The
build job of ci.yml runs them with `python3 -B -m unittest discover -s .github/repair -p 'test_*.py'`. They use
unittest, which the standard library holds, so the job installs nothing. The tests of the keys read factory.yml of
the branch."""

import json
import os
import signal
import unittest
from unittest import mock

import closing
import merge
import runs
import traveler
import verdict
from fake_github import Fail, FakeGitHub, closes
from github import GitHubError

# The module under test puts .github/shared/ on the path, so this test also runs on its own.
comments = merge.comments

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
PR_VIEW = ("pr", "view", "7", "-R", REPO, "--json", merge.FIELDS)
ARM = ("pr", "merge", "7", "-R", REPO, "--auto", "--squash", "--delete-branch", "--match-head-commit", SHA)
DISARM = ("pr", "merge", "7", "-R", REPO, "--disable-auto")
VERDICT = ("api", f"repos/{REPO}/commits/{SHA}/check-runs?check_name={verdict.CHECK}&filter=latest")
COMMENT = ("api", "-X", "POST", comments.path(REPO, 5), "--input", "-")
PR_COMMENT = ("api", "-X", "POST", comments.path(REPO, 7), "--input", "-")
# The verb of a dispatch, one word on each line, so the grep of #516's `## Done when` matches runs.py alone.
START = (
    "workflow",
    "run",
)
FIELDS = runs.FIELDS
GATE_LIST = (
    "run",
    "list",
    "-R",
    REPO,
    "--workflow",
    merge.WORKFLOW,
    "--branch",
    "repair/5",
    "--event",
    "workflow_dispatch",
    "--limit",
    "30",
    "--json",
    FIELDS,
)
GATE = (*START, merge.WORKFLOW, "-R", REPO, "--ref", "repair/5", "-f", "issue=451", "-f", "base=main")
GATE_VIEW = ("run", "view", "1", "-R", REPO, "--json", "status,conclusion")
CI = (*START, "ci.yml", "-R", REPO, "--ref", "main")
# A `createdAt` far in the future, so it is after the `since` of each dispatch.
NOW = "2100-01-01T00:00:00Z"
CLOSE = (
    "issue",
    "close",
    "5",
    "-R",
    REPO,
    "--comment",
    f"Merged as `{MERGE_SHA}` by pull request #7, merged by `github-actions`.",
)


def raw(ref):
    return ("api", "-H", "Accept: application/vnd.github.raw+json", f"repos/{REPO}/contents/{merge.FACTORY}?ref={ref}")


class FileAtTests(unittest.TestCase):
    def test_an_absent_file_is_none(self):
        gh = FakeGitHub()
        gh.answer(raw(SHA), Fail(status=404))
        self.assertIsNone(merge.file_at(gh, REPO, merge.FACTORY, SHA))

    def test_any_other_failure_ends_the_job(self):
        for status in (None, 502):
            with self.subTest(status=status):
                gh = FakeGitHub()
                gh.answer(raw(SHA), Fail(status=status))
                with self.assertRaises(GitHubError):
                    merge.file_at(gh, REPO, merge.FACTORY, SHA)

    def test_a_present_file_is_its_text(self):
        gh = FakeGitHub()
        gh.answer(raw(SHA), FACTORY_TEXT)
        self.assertEqual(merge.file_at(gh, REPO, merge.FACTORY, SHA), FACTORY_TEXT)


class ChangedFilesTests(unittest.TestCase):
    def test_a_rename_gives_both_paths(self):
        gh = FakeGitHub()
        gh.answer_list(
            f"repos/{REPO}/pulls/7/files",
            [
                {"filename": "src/moved.py", "previous_filename": ".github/repair/merge.py"},
                {"filename": ".github/workflows/factory.yml"},
            ],
        )
        count, paths = merge.changed_files(gh, REPO, 7)
        self.assertEqual(count, 2)
        self.assertEqual(paths, ["src/moved.py", ".github/repair/merge.py", ".github/workflows/factory.yml"])
        self.assertEqual(merge.guarded(paths), [".github/repair/merge.py"])


class BaseTests(unittest.TestCase):
    """main() over a FakeGitHub that answers every read and write of the step."""

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
            closing.FIELD: closes(REPO, 5) if base == "main" else [],
            "changedFiles": 1,
            **fields,
        }

    def traveler_body(self, **fields):
        ticket = traveler.Traveler(
            run=f"https://github.com/{REPO}/actions/runs/9", target="`repair/9`", sha=SHA, session="sesn_1", **fields
        )
        return f"The fix.\n\n{traveler.render(ticket)}"

    def run_main(
        self, *bases, paths=("src/x.cs",), issue=None, red_branch="main", gate=("c" * 40,), env=None, verdicts=None
    ):
        """The fake after main() ran. Each read of the pull request gives the next base, and the read after the arm
        gives it merged. `gate` is the head SHA of the gate run, then each status of it in turn, and its
        conclusion. `verdicts` are the check runs of judge-verdict in turn, a completed success by default."""
        gh = FakeGitHub()
        merged = self.pull_request("main", state="MERGED", mergeCommit={"oid": MERGE_SHA})
        merged["mergedBy"] = {"login": "github-actions"}
        gh.answer(PR_VIEW, *[self.pull_request(base) for base in bases], merged)
        gh.answer(("api", f"repos/{REPO}", "--jq", ".default_branch"), "main\n")
        gh.answer(("issue", "view", "5", "-R", REPO, "--json", "body,labels"), issue or {"body": "", "labels": []})
        gh.answer_list(f"repos/{REPO}/pulls/7/files", [{"filename": path} for path in paths])
        gh.answer(raw(BASE_SHA), FACTORY_TEXT)
        gh.answer(raw(SHA), FACTORY_TEXT)
        gh.answer(("api", f"repos/{REPO}/actions/runs/9"), {"head_branch": red_branch})
        gh.answer(VERDICT, *(verdicts or [{"check_runs": [{"status": "completed", "conclusion": "success"}]}]))
        self.take_comments(gh)
        gate_run = {"databaseId": 1, "headSha": gate[0], "displayTitle": "#451", "url": "u", "createdAt": NOW}
        gh.answer(GATE_LIST, [], [gate_run])
        gh.answer(GATE, "")
        gh.answer(GATE_VIEW, *[{"status": status, "conclusion": conclusion} for status, conclusion in gate[1:]])
        gh.answer(ARM, "")
        gh.answer(CI, "")
        gh.answer(("issue", "view", "5", "-R", REPO, "--json", "state"), {"state": "OPEN"})
        gh.answer(CLOSE, "")
        gh.answer(("api", f"repos/{REPO}/git/matching-refs/heads/repair/5"), [])
        env = {"REPAIR_ENABLED": "true", "GITHUB_REPOSITORY": REPO, "REPAIR_POLL_SECONDS": "0", **(env or {})}
        with (
            mock.patch.dict(os.environ, env),
            mock.patch.object(merge.sys, "argv", ["merge.py", "7", SHA]),
            mock.patch.object(runs.time, "sleep"),
        ):
            merge.main(gh)
        return gh

    @staticmethod
    def take_comments(gh):
        """Issue 5 and pull request 7 take the comments of comments.py, and each list holds each one posted there."""
        gh.answer_list(comments.path(REPO, 5), lambda: [{"body": text} for text in BaseTests.comments(gh)])
        gh.answer(COMMENT, "{}")
        gh.answer_list(comments.path(REPO, 7), lambda: [{"body": text} for text in BaseTests.comments(gh, PR_COMMENT)])
        gh.answer(PR_COMMENT, "{}")

    @staticmethod
    def comments(gh, post=COMMENT):
        return [json.loads(text)["body"] for args, text in gh.calls if args == post]

    def test_a_second_stop_on_the_head_posts_no_second_comment(self):
        gh = FakeGitHub()
        self.take_comments(gh)
        for _ in range(2):
            merge.stop(gh, REPO, 7, 5, SHA, "No merge.")
        [text] = self.comments(gh)
        self.assertIn(f"repair-merge:{SHA}", text.splitlines()[0])
        self.assertEqual(gh.count("api_list", comments.path(REPO, 5)), 2)

    def test_a_pull_request_on_main_arms_the_merge_and_closes_the_issue(self):
        gh = self.run_main("main", "main", "main")
        self.assertEqual(self.comments(gh), [])
        self.assertEqual(gh.count(*ARM), 1)
        self.assertEqual(gh.count(*CLOSE), 1)

    def test_a_pull_request_on_another_base_is_refused_with_one_comment_on_the_pull_request(self):
        gh = self.run_main("dev")
        self.assertEqual(self.comments(gh), [])
        [text] = self.comments(gh, PR_COMMENT)
        self.assertIn("Pull request #7 targets `dev`, not `main`.", text)
        self.assertEqual(gh.count("api_list", f"repos/{REPO}/pulls/7/files"), 0)
        self.assertEqual(gh.count(*ARM), 0)

    def test_a_retarget_while_the_job_reads_the_files_merges_nothing(self):
        gh = self.run_main("main", "dev")
        self.assertEqual(gh.count("api_list", f"repos/{REPO}/pulls/7/files"), 1)
        self.assertEqual(self.comments(gh), [])
        self.assertEqual(gh.count(*VERDICT), 0)
        self.assertEqual(gh.count(*ARM), 0)

    def test_a_retarget_before_the_arm_merges_nothing(self):
        gh = self.run_main("main", "main", "dev")
        self.assertEqual(gh.count(*VERDICT), 1)
        self.assertEqual(self.comments(gh), [])
        self.assertEqual(gh.count(*ARM), 0)

    def test_a_factory_diff_with_no_key_change_arms_the_merge(self):
        gh = self.run_main("main", "main", "main", paths=[merge.FACTORY])
        self.assertEqual(self.comments(gh), [])
        self.assertEqual((gh.count(*raw(BASE_SHA)), gh.count(*raw(SHA))), (1, 1))
        self.assertEqual(gh.count(*ARM), 1)

    def test_a_guarded_path_is_refused_with_one_comment_before_the_judge(self):
        gh = self.run_main("main", "main", paths=[".github/workflows/judge.yml"])
        [text] = self.comments(gh)
        self.assertIn("`.github/workflows/judge.yml`", text)
        self.assertEqual(gh.count(*VERDICT), 0)
        self.assertEqual(gh.count(*ARM), 0)

    def test_the_branch_of_the_red_run_in_the_traveler_is_refused_with_one_comment(self):
        gh = self.run_main("main", "main", issue={"body": self.traveler_body(), "labels": []}, red_branch="repair/5")
        [text] = self.comments(gh)
        self.assertIn("is the branch of the red run", text)
        self.assertEqual(gh.count(*VERDICT), 0)

    def factory_repair(self, *bases, gate, env=None):
        issue = {"body": self.traveler_body(story=451), "labels": [{"name": "factory"}]}
        return self.run_main(*bases, issue=issue, gate=gate, env=env)

    def test_a_factory_repair_dispatches_the_gate_on_the_story_of_its_traveler(self):
        gh = self.factory_repair("main", "main", gate=("c" * 40,))
        self.assertEqual(self.comments(gh), [])
        self.assertEqual(gh.count(*GATE), 1)
        self.assertEqual(gh.count(*GATE_VIEW), 0)
        self.assertEqual(gh.count(*ARM), 0)

    def test_a_green_gate_run_on_the_settled_head_arms_the_merge(self):
        gh = self.factory_repair("main", "main", "main", gate=(SHA, ("queued", ""), ("completed", "success")))
        self.assertEqual(self.comments(gh), [])
        self.assertEqual(gh.count(*GATE), 1)
        self.assertEqual(gh.count(*GATE_VIEW), 2)
        self.assertEqual(gh.count(*ARM), 1)
        self.assertEqual(gh.count(*CI), 1)
        self.assertEqual(gh.count(*CLOSE), 1)

    def test_a_red_gate_run_is_one_comment_and_no_merge(self):
        gh = self.factory_repair("main", "main", gate=(SHA, ("completed", "failure")))
        [text] = self.comments(gh)
        self.assertIn("The gate run u of the story #451", text)
        self.assertIn("is `failure`", text)
        self.assertEqual(gh.count(*ARM), 0)

    def test_a_gate_run_in_progress_at_the_timeout_exits_and_arms_no_merge(self):
        with self.assertRaises(SystemExit) as stop:
            self.factory_repair(
                "main", "main", gate=(SHA, ("in_progress", "")), env={"REPAIR_GATE_TIMEOUT_SECONDS": "0"}
            )
        self.assertIn("the gate run", stop.exception.code)
        self.assertIn("after the timeout", stop.exception.code)

    def test_a_factory_repair_whose_traveler_names_no_story_is_refused_with_one_comment(self):
        issue = {"body": self.traveler_body(pull_request=3), "labels": [{"name": "factory"}]}
        gh = self.run_main("main", "main", issue=issue)
        [text] = self.comments(gh)
        self.assertIn("the traveler of its issue names no story", text)
        self.assertEqual(gh.count(*VERDICT), 0)

    # The wait for judge-verdict, through runs.poll: an absent check and one in progress are waited on.
    ABSENT = {"check_runs": []}
    RUNNING = {"check_runs": [{"status": "in_progress", "conclusion": None}]}
    PASSED = {"check_runs": [{"status": "completed", "conclusion": "success"}]}

    def test_an_absent_then_running_check_is_waited_on_and_the_merge_arms(self):
        gh = self.run_main("main", "main", "main", verdicts=[self.ABSENT, self.RUNNING, self.PASSED])
        self.assertEqual(gh.count(*VERDICT), 3)
        self.assertEqual(self.comments(gh), [])
        self.assertEqual(gh.count(*ARM), 1)

    def test_an_absent_check_at_the_timeout_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.run_main("main", "main", verdicts=[self.ABSENT], env={"REPAIR_MERGE_TIMEOUT_SECONDS": "0"})
        self.assertEqual(stop.exception.code, f"repair: {verdict.CHECK} on {SHA[:7]} is absent after the timeout")

    def test_a_running_check_at_the_timeout_exits(self):
        with self.assertRaises(SystemExit) as stop:
            self.run_main("main", "main", verdicts=[self.RUNNING], env={"REPAIR_MERGE_TIMEOUT_SECONDS": "0"})
        self.assertEqual(stop.exception.code, f"repair: {verdict.CHECK} on {SHA[:7]} is in_progress after the timeout")


class WaitForMergeTests(unittest.TestCase):
    def wait(self, base, head=SHA, disarm=""):
        gh = FakeGitHub()
        gh.answer(PR_VIEW, {"state": "OPEN", "headRefOid": head, "baseRefName": base})
        gh.answer(DISARM, disarm)
        return merge.wait_for_merge(gh, REPO, 7, SHA, "main", 0, 0), gh

    def test_a_retarget_while_armed_disarms(self):
        result, gh = self.wait("dev")
        self.assertIsNone(result)
        self.assertEqual(gh.count(*DISARM), 1)

    def test_a_moved_head_while_armed_disarms(self):
        result, gh = self.wait("main", head="c" * 40)
        self.assertIsNone(result)
        self.assertEqual(gh.count(*DISARM), 1)

    def test_a_failed_disarm_is_a_log_line(self):
        result, gh = self.wait("dev", disarm=Fail(status=502))
        self.assertIsNone(result)
        self.assertEqual(gh.count(*DISARM), 1)

    def test_an_unmoved_pull_request_waits_to_the_timeout(self):
        with self.assertRaises(SystemExit):
            self.wait("main")


if __name__ == "__main__":
    unittest.main()
