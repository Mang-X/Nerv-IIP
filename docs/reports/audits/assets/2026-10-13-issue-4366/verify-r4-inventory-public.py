"""核对 #4366 R1 后新会话的公开库存计价见证；不合并旧会话的业务事实。"""

import argparse
import hashlib
import json
from decimal import Decimal
from pathlib import Path


parser = argparse.ArgumentParser()
parser.add_argument('--input', type=Path, default=Path(__file__).with_name('r4-inventory-public-evidence.json'))
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
pack = json.loads(args.input.read_text())
read = lambda name: json.loads(pack['sourceFiles'][name + '.json'])
D = lambda value: Decimal(str(value))
checks = []


def check(name, condition):
    passed = bool(condition)
    checks.append({'check': name, 'passed': passed})
    print(('PASS ' if passed else 'FAIL ') + name, flush=True)


for name, value in pack['sourceFiles'].items():
    check('archive member hash ' + name, hashlib.sha256(value.encode()).hexdigest() == pack['sourceArchiveMemberSha256'][name])

run = read('runtime-identity')
check('independent fixed SHA and session', run['runSha'] == 'f2d66acbcba3b735f5cee14f48c7b02f92a4f743' and run['sessionId'] == 'nerv-0d43-b8f062')
roles = read('roles')
check('six distinct scoped UI business principals', len(roles) == 6 and len({x['principal']['principalId'] for x in roles.values()}) == 6 and all(x['principal']['organizationId'] == 'org-001' and x['principal']['environmentId'] == 'env-dev' for x in roles.values()) and all(len(roles[k]['scopeBindings']) == 8 and all(s['scopeType'] == 'work-center' for s in roles[k]['scopeBindings']) for k in ['operator', 'handover']))

writes = pack['uiWrites']
check('true-chain UI writes have original request response and correlation', bool(writes) and all(r['method'] != 'GET' and r['status'] < 300 and 'request' in r and r['response'] is not None and r['correlationId'] for r in writes))
check('MPS opening public reads empty', all(read(n)['items'] == [] for n in ['real-before-mps', 'real-before-mrp', 'real-before-suggestions']))
m0 = read('real-01-mps-draft')['items'][0]
m1 = read('real-02-mps-reviewed')['items'][0]
m2 = read('real-03-mps-released')['items'][0]
check('same UI MPS draft review release with quantity2', m0['mpsId'] == m1['mpsId'] == m2['mpsId'] and [m0['status'], m1['status'], m2['status']] == ['Draft', 'Reviewed', 'Released'] and D(m2['quantity']) == 2 and m2['reviewedBy'] == m2['releasedBy'] == roles['planner']['principal']['principalId'])
mrp = read('real-04-mrp-completed')['items'][0]
s = next(x for x in read('real-05-accepted')['items'] if x['skuCode'] == m2['skuCode'])
wo = read('real-05-wo-created')
check('same MPS MRP accepted suggestion to WO', mrp['status'] == 'Completed' and mrp['inputSources'] == ['mps'] and not mrp['hasInputDegradation'] and mrp['suggestionCount'] == 13 and s['runId'] == mrp['runId'] and s['status'] == 'Accepted' and s['downstreamDocumentId'] == wo['workOrderId'] and wo['sourcePlanReference']['sourceDocumentId'] == s['suggestionId'] and wo['sourcePlanReference']['sourceDemandReference'] == 'MPS:' + m2['mpsId'])

mir = next(r['response']['data'] for r in pack['uiPublicReadbacks'] if 'mes/material-issue-requests?' in r['url'])
check('same WO eleven material receipts and Ready', len(mir['items']) == 11 and all(x['workOrderId'] == wo['workOrderId'] and x['status'] == 'Received' and D(x['receivedQuantity']) == D(x['requestedQuantity']) for x in mir['items']) and read('real-09-material-ready-terminal')['readinessStatus'] == 'Ready')
released = read('real-18-plan-released')[0]
check('quality-triggered schedule invalidation recovered for OP80', read('real-17-op70-completed')['operationTasks'][-1]['status'] == 'ScheduleInvalidated' and released['status'] == 'released' and not released['isInvalidated'] and released['assignmentCount'] == 8 and read('real-18-wo-op80-ready')['operationTasks'][-1]['status'] == 'Queued')
quality = read('real-16-quality-records')['items'][0]
completed = read('real-19-all-ops-completed')
check('operation inspection and all eight UI operations complete same WO', quality['status'] == 'passed' and quality['sourceDocumentId'] == wo['workOrderId'] and quality['skuCode'] == m2['skuCode'] and completed['workOrderId'] == wo['workOrderId'] and completed['status'] == 'completed' and D(completed['completedQuantity']) == D(m2['quantity']) and len(completed['operationTasks']) == 8 and all(x['status'] == 'Completed' for x in completed['operationTasks']))

report = next(r['response']['data']['report'] for r in pack['uiPublicReadbacks'] if 'production-reports/PRPT-20261013-000008' in r['url'])
lot = next(r['response']['data']['items'][0] for r in pack['uiPublicReadbacks'] if '/produced-lots?' in r['url'])
fgr_write = next(r for r in writes if r['method'] == 'POST' and r['url'].split('?')[0].endswith('/mes/finished-goods-receipt-requests'))
before = next(x for x in read('real-21-fgr-requested')['items'] if x['workOrderId'] == wo['workOrderId'])
after = next(x for x in read('real-23-fgr-after-erp')['items'] if x['receiptRequestId'] == before['receiptRequestId'])
check('UI OP80 report and public produced lot match FGR input', report['operationTaskId'] == wo['workOrderId'] + '-OP-80' and report['workOrderId'] == wo['workOrderId'] and D(report['goodQuantity']) == D(m2['quantity']) and lot['reportNo'] == report['reportNo'] and lot['operationTaskId'] == report['operationTaskId'] and lot['producedLotNo'] == report['producedLotNo'] == fgr_write['request']['producedLotNo'] == before['producedLotNo'] and D(lot['quantity']) == D(m2['quantity']))
check('ERP-stop FGR waiting and no final Inventory movement', before['receiptStatus'] == 'Requested' and before['unitCost'] is None and before['postedInventoryMovementId'] is None and not any(x['sourceDocumentId'] == before['requestNo'] for x in read('real-21-before-erp-movements')['items']))

movement_id = after['postedInventoryMovementId']
movements = [x for x in read('real-23-movements-after-erp')['items'] if x['movementId'] == movement_id]
movement = movements[0] if len(movements) == 1 else None
link = read('real-24-link-final')
stock = read('real-24-inventory-availability')
cost = read('real-24-cost-final')
check('same FGR Posted and exact movement strong IDs', after['receiptStatus'] == 'Posted' and before['requestNo'] == after['requestNo'] == link['requestNo'] and after['workOrderId'] == link['workOrderId'] == wo['workOrderId'] and after['producedLotNo'] == link['producedLotNo'] == lot['producedLotNo'] and D(after['postedQuantity']) == D(m2['quantity']) and D(after['remainingQuantity']) == 0 and link['isInventoryLinkEstablished'] and link['postedInventoryMovementId'] == movement_id and movement is not None and movement['sourceService'] == 'business-mes' and movement['sourceDocumentId'] == after['requestNo'] and movement['sourceDocumentLineId'] == wo['workOrderId'] and movement['lotNo'] == lot['producedLotNo'] and movement['skuCode'] == m2['skuCode'])
check('public Inventory quantity unit cost and amount agree with FGR and ERP', movement is not None and D(movement['quantity']) == D(after['postedQuantity']) == D(m2['quantity']) and D(movement['unitCost']) == D(after['unitCost']) and D(movement['movementAmount']) == D(movement['unitCost']) * D(movement['quantity']) == D(cost['capitalizedCost']) and D(cost['totalAccumulatedCost']) == D(cost['capitalizedCost']) + D(cost['capitalizationVarianceAmount']))
check('public lot stock same quantity and location', stock['skuCode'] == movement['skuCode'] and D(stock['onHandQuantity']) == D(movement['quantity']) and any(x['lotNo'] == movement['lotNo'] and x['locationCode'] == movement['locationCode'] and D(x['onHandQuantity']) == D(movement['quantity']) for x in stock['items']))

public = pack['publicReadEnvelopes']
before_raw = next((r for r in public if 'inventory/movements' in r['path'] and r['atUtc'] < '2026-10-13T07:05:30+00:00' and r['status'] == 200 and not any(x['sourceDocumentId'] == before['requestNo'] for x in r['response']['data']['items'])), None)
after_raw = next((r for r in public if 'inventory/movements' in r['path'] and r['atUtc'] > '2026-10-13T07:05:49+00:00' and r['status'] == 200 and any(x['movementId'] == movement_id for x in r['response']['data']['items'])), None)
check('raw public Inventory before and after GET carry the exact priced movement', before_raw is not None and after_raw is not None and before_raw['correlationId'] and after_raw['correlationId'] and next(x for x in after_raw['response']['data']['items'] if x['movementId'] == movement_id) == movement)
after_fgr_raw = next((r for r in public if r['path'].split('?')[0].endswith('/mes/finished-goods-receipt-requests') and r['status'] == 200 and any(x['receiptRequestId'] == after['receiptRequestId'] and x['receiptStatus'] == 'Posted' for x in r['response']['data']['items'])), None)
cost_raw = next((r for r in public if '/erp/finance/work-order-costs/' + wo['workOrderId'] in r['path'] and r['status'] == 200), None)
check('raw public FGR and ERP readbacks equal saved terminal states', after_fgr_raw is not None and cost_raw is not None and after_fgr_raw['correlationId'] and cost_raw['correlationId'] and next(x for x in after_fgr_raw['response']['data']['items'] if x['receiptRequestId'] == after['receiptRequestId']) == after and cost_raw['response']['data'] == cost)
events = pack['serviceEventRecords']
check('ERP MES Inventory subscriber and movement amount service evidence', len(events) == 5 and all(x['record']['Timestamp'] for x in events) and all('Executed subscriber method' in x['record']['RenderedMessage'] for x in events[:3]) and after['requestNo'] in events[3]['record']['RenderedMessage'] and str(movement['unitCost']) in events[4]['record']['RenderedMessage'] and str(movement['movementAmount']) in events[4]['record']['RenderedMessage'])
cleanup = read('cleanup-readback')
check('own runtime Stopped and no owned resources', run['state'] == 'Stopped' and run['cleanup']['remaining'] == [] and run['cleanup']['errors'] == [] and all(cleanup[k] == [] for k in ['containers', 'volumes', 'networks']) and not cleanup['observerPort52293Listening'] and cleanup['previewClosed'])
check('exact private password scan zero', all(n == 0 for n in read('password-value-scan')['exactPasswordValueHits'].values()))

result = {'runSha': run['runSha'], 'sessionId': run['sessionId'], 'checks': checks, 'passed': sum(x['passed'] for x in checks), 'failed': sum(not x['passed'] for x in checks), 'publicInventoryValuationWitnessVerified': all(x['passed'] for x in checks), 'whole4366Accepted': False, 'independentReviewRequired': True, 'uiWriteCount': len(writes), 'unverified': pack['unverified'], 'evidencePlane': 'new independent runtime plus archive validation; not the old 9aa session or a repository gate'}
args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
print('R4 public Inventory witness verified; #4366 completion awaits independent review', flush=True)
raise SystemExit(0 if result['publicInventoryValuationWitnessVerified'] else 1)
