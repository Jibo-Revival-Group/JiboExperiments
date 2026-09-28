"""Prevent local credentials/captures from entering Docker's COPY context."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class DockerBuildContextTests(unittest.TestCase):
    def test_sensitive_directory_rules_are_present(self):
        rules = (ROOT / '.dockerignore').read_text().splitlines()
        for rule in ('**/.env', '**/.env.*', '!**/.env.example', '**/captures/',
                     '**/artifact-output/', '**/backups/', '**/node_modules/'):
            self.assertIn(rule, rules)

    @unittest.skipUnless(os.environ.get('OPENJIBO_TEST_DOCKER_CONTEXT') == '1',
                         'opt-in scratch Docker build; no image pull required')
    def test_docker_excludes_synthetic_private_files(self):
        with tempfile.TemporaryDirectory(prefix='openjibo-context-') as temporary:
            base = Path(temporary)
            context = base / 'context'
            context.mkdir()
            shutil.copyfile(ROOT / '.dockerignore', context / '.dockerignore')
            (context / 'Dockerfile').write_text('FROM scratch\nCOPY . /context/\n')
            kept = ['.env.example', 'src/.env.example', 'src/required.cs', 'scripts/entry.sh']
            excluded = ['.env', '.env.production', 'src/.env', 'src/.env.local',
                        'captures/robot.log', 'artifact-output/backup.zip',
                        'backups/db.dump', 'src/node_modules/vendor/index.js',
                        'src/captures/robot.log', 'src/artifact-output/data.json']
            for name in kept + excluded:
                path = context / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('synthetic fixture only\n')
            output = base / 'export'
            result = subprocess.run(
                ['docker', 'build', '--network=none', '--progress=plain',
                 '--output', f'type=local,dest={output}', str(context)],
                capture_output=True, text=True, timeout=120, check=False)
            self.assertEqual(result.returncode, 0, result.stderr)
            for name in kept:
                self.assertTrue((output / 'context' / name).is_file(), name)
            for name in excluded:
                self.assertFalse((output / 'context' / name).exists(), name)


if __name__ == '__main__':
    unittest.main()
