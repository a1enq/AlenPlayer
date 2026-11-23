# Checks that every L.T("ru", "en") string in src/ has translations in tools/translations.py.
import glob, os, re, sys

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(root, 'tools'))
from translations import T

txt = ''.join(open(f, encoding='utf-8').read() for f in glob.glob(os.path.join(root, 'src', '*.cs'))
              if not os.path.basename(f).startswith('Lang'))
STR = r'"(?:[^"\\]|\\.)*"'
ARG = r'((?:' + STR + r'\s*\+?\s*)+)'
calls = re.findall(r'L\.T\(' + ARG + r',\s*' + ARG + r'\)', txt)


def join(x):
    return ''.join(re.findall(r'"((?:[^"\\]|\\.)*)"', x)).replace('\\n', '\n').replace('\\"', '"')


code = {join(en) for _, en in calls}
missing = [k for k in code if k not in T]
unused = [k for k in T if k not in code]
print(len(calls), 'calls,', len(code), 'unique strings')
print('missing translations:', missing or 'none')
print('unused translations:', [k[:40] for k in unused] or 'none')
sys.exit(1 if missing else 0)
