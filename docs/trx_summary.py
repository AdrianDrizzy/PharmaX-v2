"""Summarises TRX test results by test class, for the acceptance report."""
import sys, json, collections
from xml.etree import ElementTree as ET

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
out = collections.OrderedDict()
totals = {'passed': 0, 'failed': 0, 'total': 0}

for path in sys.argv[1:-1]:
    tree = ET.parse(path)
    defs = {}
    for d in tree.findall('.//t:UnitTest', NS):
        tm = d.find('t:TestMethod', NS)
        if tm is None:
            continue
        defs[d.get('id')] = (tm.get('className', ''), tm.get('name', ''))
    for r in tree.findall('.//t:UnitTestResult', NS):
        cls, _ = defs.get(r.get('testId'), ('(unknown)', ''))
        cls = cls.split(',')[0]
        short = cls.split('.')[-1]
        area = '.'.join(cls.split('.')[1:-1]) or 'root'
        key = (area, short)
        rec = out.setdefault(key, {'passed': 0, 'failed': 0})
        ok = r.get('outcome') == 'Passed'
        rec['passed' if ok else 'failed'] += 1
        totals['passed' if ok else 'failed'] += 1
        totals['total'] += 1

rows = [{'area': a, 'suite': s, 'passed': v['passed'], 'failed': v['failed']}
        for (a, s), v in out.items()]
rows.sort(key=lambda r: (r['area'], r['suite']))
json.dump({'totals': totals, 'suites': rows},
          open(sys.argv[-1], 'w', encoding='utf-8'), indent=1)
print('suites: %d  passed: %d  failed: %d'
      % (len(rows), totals['passed'], totals['failed']))
