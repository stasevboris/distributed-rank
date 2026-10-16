"""Integration checks over real TCP, compared with an exact Fraction rank oracle."""
from fractions import Fraction
from pathlib import Path
import json
import random
import socket
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
DLL = ROOT / 'bin' / 'Release' / 'net8.0' / 'DistributedRank.dll'


def exact_rank(source):
    a = [[Fraction(value) for value in row] for row in source]
    rank = 0
    for col in range(len(a)):
        pivot = next((r for r in range(rank, len(a)) if a[r][col]), None)
        if pivot is None:
            continue
        a[rank], a[pivot] = a[pivot], a[rank]
        for row in range(rank + 1, len(a)):
            factor = a[row][col] / a[rank][col]
            for j in range(col, len(a)):
                a[row][j] -= factor * a[rank][j]
        rank += 1
    return rank


def check_case(name, matrix, workers=3):
    expected = exact_rank(matrix)
    with tempfile.TemporaryDirectory(prefix='rank-test-') as temp:
        temp = Path(temp)
        input_path = temp / 'matrix.txt'
        input_path.write_text('\n'.join(' '.join(map(str, row)) for row in matrix), encoding='utf-8')
        with socket.socket() as sock:
            sock.bind(('127.0.0.1', 0))
            port = sock.getsockname()[1]
        result_path = temp / 'result.json'
        master = subprocess.Popen([
            'dotnet', str(DLL), 'master', '--matrix', str(input_path),
            '--port', str(port), '--workers', str(workers), '--repeat', '2',
            '--result', str(result_path)
        ], cwd=ROOT, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding='utf-8')
        children = []
        try:
            for i in range(workers):
                children.append(subprocess.Popen([
                    'dotnet', str(DLL), 'worker', '--host', '127.0.0.1',
                    '--port', str(port), '--id', f'test-worker-{i}'
                ], cwd=ROOT, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, encoding='utf-8'))
            out, err = master.communicate(timeout=30)
            assert master.returncode == 0, (name, out, err)
            result = json.loads(result_path.read_text(encoding='utf-8'))
            assert result['rank'] == expected, (name, result['rank'], expected)
            assert 'Ranks agree: True' in out, (name, out)
            assert len(result['distributedSamples']) == 2, (name, result)
            for child in children:
                _, err = child.communicate(timeout=10)
                assert child.returncode == 0, (name, err)
            print(f'PASS {name}: rank={expected}, workers={workers}')
        finally:
            for child in [master] + children:
                if child.poll() is None:
                    child.kill()
                    child.wait(timeout=5)


def check_invalid(name, text, extra=None):
    with tempfile.TemporaryDirectory(prefix='rank-invalid-') as temp:
        p = Path(temp) / 'invalid.txt'
        p.write_text(text, encoding='utf-8')
        result = subprocess.run(['dotnet', str(DLL), 'master', '--matrix', str(p)] + (extra or []),
                                cwd=ROOT, capture_output=True, text=True, encoding='utf-8', timeout=10)
        assert result.returncode != 0 and 'Error:' in result.stderr, (name, result)
        print(f'PASS {name}: invalid input rejected')


if __name__ == '__main__':
    cases = [
        ('sample rank 3', [[1, 2, 3, 4], [2, 4, 6, 8], [0, 1, 0, 1], [0, 0, 1, 1]], 3),
        ('leading zero column', [[0, 1, 0], [0, 0, 1], [0, 2, 3]], 3),
        ('interior zero column', [[1, 0, 0], [0, 0, 1], [2, 0, 3]], 3),
        ('zero matrix', [[0, 0], [0, 0]], 3),
        ('identity', [[1, 0, 0], [0, 1, 0], [0, 0, 1]], 3),
        ('duplicate rows', [[2, 3, 4], [2, 3, 4], [4, 6, 8]], 3),
        ('one element', [[7]], 3),
        ('one zero element', [[0]], 3),
        ('swap and tied pivot', [[0, 1, 2], [2, 2, 0], [-2, 0, 3]], 3),
        ('single worker', [[0, 1], [1, 0]], 1),
        ('more workers than rows', [[0, 1], [0, 2]], 5),
    ]
    rng = random.Random(1601)
    for i in range(10):
        n = rng.randrange(3, 9)
        matrix = [[rng.randrange(-5, 6) for _ in range(n)] for _ in range(n)]
        if i % 2 == 0:
            matrix[-1] = [2 * value for value in matrix[0]]
        cases.append((f'random exact case {i + 1}', matrix, 3))
    for name, matrix, workers in cases:
        check_case(name, matrix, workers)
    check_invalid('non-square', '1 2\n3 4 5\n')
    check_invalid('non-finite', 'NaN 1\n0 1\n')
    check_invalid('empty', '# no matrix\n')
    check_invalid('invalid tolerance', '1\n', ['--tolerance', 'NaN'])
    check_invalid('missing matrix path', '1\n', ['--repeat'])
    print(f'ALL {len(cases) + 5} CHECKS PASSED')
