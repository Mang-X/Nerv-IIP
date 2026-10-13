from pathlib import Path
from decimal import Decimal
import json,sys,collections,hashlib,re,argparse
E=Path(__file__).parent
parser=argparse.ArgumentParser();parser.add_argument('--output',required=True);args=parser.parse_args()
pack=json.loads((E/'evidence.json').read_text());raw=pack['sourceFiles']
for name,value in raw.items():
 assert hashlib.sha256(value.encode()).hexdigest()==pack['sourceFileHashes'][name], name
assert hashlib.sha256((E/'original-verifier.py').read_bytes()).hexdigest()==pack['originalVerifierSha256']
read=lambda n:json.loads(raw[n+'.json'])
checks=[]
def check(name,value):
 checks.append({'check':name,'passed':bool(value)})
 print(('PASS ' if value else 'FAIL ')+name,flush=True)
sha=read('source-version')['runSha'];check('exact run SHA',sha=='9aa493c14028ba5984f4f61da7b47525406d5c63')
roles=read('roles');workers=read('workers-current')['items'];check('six distinct public principals linked to actual active workers',len(roles)==6 and len({r['principal']['principalId'] for r in roles.values()})==6 and all(any(w.get('userId')==r['principal']['principalId'] and w.get('employmentStatus')=='active' for w in workers) for r in roles.values()))
check('operation and handover only eight work-center grants',all(len(roles[k]['scopeBindings'])==8 and all(s['scopeType']=='work-center' for s in roles[k]['scopeBindings']) for k in ['operator','handover']))
ui=[json.loads(x) for x in raw['ui-network.jsonl'].splitlines()];writes=[r for r in ui if r['method']!='GET'];check('all82 actual UI write receipts have payload response correlation',len(writes)==82 and all('request' in r and 'response' in r and r['response'] is not None and r['correlationId'] for r in writes))
check('no HTTP5xx UI write',all(r['status']<500 for r in writes))
for n in ['before-planning-mps','before-planning-mrp-runs','before-planning-suggestions','before-mes-work-orders']:check(n+' empty',read(n)['items']==[])
m=read('03-mps-released')['items'][0];mid=m['mpsId'];check('same MPS Draft Reviewed Released with planner identity',read('01-mps-draft')['items'][0]['status']=='Draft' and read('02-mps-reviewed')['items'][0]['status']=='Reviewed' and m['status']=='Released' and m['reviewedBy']==m['releasedBy']==roles['planner']['principal']['principalId'])
r=read('04-mrp-completed')['items'][0];check('MRP Completed MPS input no degradation13 suggestions',r['status']=='Completed' and r['inputSources']==['mps'] and not r['hasInputDegradation'] and r['suggestionCount']==13)
s=next(x for x in read('05-suggestion-accepted')['items'] if x['skuCode']=='FG-QJ-P1-L');d=read('05-work-order-created');check('accepted suggestion creates same WO and same MPS lineage',s['status']=='Accepted' and s['downstreamDocumentId']==d['workOrderId'] and d['sourcePlanReference']['sourceDocumentId']==s['suggestionId'] and d['sourcePlanReference']['sourceDemandReference']=='MPS:'+mid)
mi=read('10-material-all-received')['items'];check('11 exact received quantities and material Ready',len(mi)==11 and all(x['status']=='Received' and x['receivedQuantity']==x['requestedQuantity'] for x in mi) and read('10-material-ready')['readinessStatus']=='Ready')
check('rejected receive kept first MIR requested0',read('07-first-receive-rejected-readback')['items'][0]['status']=='Requested' and read('07-first-receive-rejected-readback')['items'][0]['receivedQuantity']==0)
q=read('14-operation-inspection-terminal')['items'];check('UI operation inspection actual passed and correct SKU',len(q)==1 and q[0]['status']=='passed' and q[0]['skuCode']=='FG-QJ-P1-L' and q[0]['sourceDocumentId']==d['workOrderId'])
n=read('15-ncr-closed-terminal');check('actual sort-and-screen closed NCR and MES return',n['status']=='closed' and n['dispositionType']=='sort-and-screen' and read('15-mes-disposition-terminal')['items'][0]['status']=='DispositionAccepted')
pre=read('18-pre-handover-unfinished');post=read('19-post-accept-same-unfinished');h0=read('18-handover-open-detail');h=read('19-handover-accepted-detail');check('real WO OP80 InProgress during handover and after incoming accept',pre['status']=='started' and pre['completedQuantity']==0 and pre['operationTasks'][-1]['status']=='InProgress' and post['workOrderId']==pre['workOrderId'] and post['operationTasks'][-1]['status']=='InProgress')
check('two principals accepted same nonempty manual WIP and unfinished snapshots',h['handoverStatus']=='Accepted' and h['outgoingUserId']==roles['operator']['principal']['principalId'] and h['incomingUserId']==roles['handover']['principal']['principalId'] and h0['wipItems']==h['wipItems']==[{'workOrderId':d['workOrderId'],'operationTaskId':d['workOrderId']+'-OP-80','quantity':2.0}] and h0['unfinishedWorkOrders']==h['unfinishedWorkOrders']==[{'workOrderId':d['workOrderId'],'plannedQuantity':2.0,'completedQuantity':0.0,'workOrderStatus':'started'}])
f=read('20-work-order-completed');lot=read('20-produced-lots-finance')['items'][0];check('same WO completed2 all8 Completed and produced batch from OP80',f['workOrderId']==d['workOrderId'] and f['status']=='completed' and f['completedQuantity']==2 and len(f['operationTasks'])==8 and all(x['status']=='Completed' for x in f['operationTasks']) and lot['operationTaskId']==h['wipItems'][0]['operationTaskId'] and lot['quantity']==2)
b=read('21-fgr-requested-authority')['items'][0];a=read('22-fgr-recovered')['items'][0];link=read('22-fgr-inventory-link');bal=read('22-fg-availability');check('ERP stop requested null cost notPosted0 same lot',b['receiptStatus']=='Requested' and b['unitCost'] is None and read('21-fgr-inventory-not-posted')['movements']==[] and b['producedLotNo']==lot['producedLotNo'])
check('actual Posted same FGR same lot inventory2 remaining0',a['receiptStatus']=='Posted' and a['requestNo']==b['requestNo'] and a['producedLotNo']==lot['producedLotNo']==link['producedLotNo']==bal['lotNo'] and a['postedQuantity']==2 and a['remainingQuantity']==0 and link['isInventoryLinkEstablished'] and bal['onHandQuantity']==bal['availableQuantity']==2 and link['movements'][0]['movementId']==a['postedInventoryMovementId'])
c=read('24-cost-final-authority');delta=Decimal(str(c['capitalizationVarianceAmount']));last=next(x for x in c['machineOverheadOperations'] if x['operationTaskId']==d['workOrderId']+'-OP-80');check('cost all8 actual machine settlements available and original period open',c['machineCostStatus']=='available' and c['totalMachineOverheadOperations']==8 and all(x['status']=='available' for x in c['machineOverheadOperations']) and read('22-period-rate-readback')['accountingPeriodStatus']=='open')
check('observed stable delta equals last operation machine overhead',delta==Decimal(str(last['appliedMachineOverheadTotal']))==Decimal('0.859137') and all(read(x)['capitalizationVarianceAmount']==float(delta) for x in ['22-erp-work-order-cost','23-erp-cost-stable-readback']))
# Sources: approved #4288 r1/#4366, ERP existing final-receipt accounting semantics.
accounting=pack['accounting'];record=accounting['actualSqlRecord'];props=record['Properties']
params=dict(re.findall(r"(@p\d+)='([^']*)'",props['parameters']));lines=[]
for columns,values in re.findall(r'INSERT INTO erp.journal_voucher_lines \(([^)]*)\)\s*VALUES \(([^)]*)\)',props['commandText']):
 lines.append(dict(zip(columns.split(', '),[params[x] for x in values.split(', ')])))
fg=next(x for x in lines if x['account_code']=='1406-FINISHED-GOODS');wip=next(x for x in lines if x['account_code']=='1405-WIP');variance=next(x for x in lines if x['account_code']=='5101-PRODUCTION-VARIANCE')
D=lambda value:Decimal(str(value))
check('public accumulated equals capitalized plus public variance',D(c['totalAccumulatedCost'])==D(c['capitalizedCost'])+D(c['capitalizationVarianceAmount']))
check('same movement sources actual voucher and public FGR inventory',params['@p33']==a['postedInventoryMovementId']==link['movements'][0]['movementId'] and params['@p34']=='WOC')
check('actual voucher debit equals credit',sum(D(x['debit_amount']) for x in lines)==sum(D(x['credit_amount']) for x in lines))
check('actual finished goods debit matches public unit cost and quantity',D(fg['debit_amount'])==D(a['unitCost'])*D(a['postedQuantity'])==D(c['capitalizedCost']) and D(fg['credit_amount'])==0)
check('actual variance debit matches public delta and late OP80',D(variance['debit_amount'])==D(c['capitalizationVarianceAmount'])==D(last['appliedMachineOverheadTotal']) and D(variance['credit_amount'])==0)
check('actual WIP credit and aggregate clearance cover accumulated cost',D(wip['credit_amount'])==D(params['@p46'])==D(c['totalAccumulatedCost']) and D(wip['debit_amount'])==0)
inv=accounting['actualInventoryValuationSqlRecord'];ip=dict(re.findall(r"(@p\d+)='([^']*)'",inv['Properties']['parameters']))
check('actual inventory valuation matches same public lot quantity and unit cost',ip['@p25']==a['postedInventoryMovementId'] and ip['@p9']==lot['producedLotNo'] and D(ip['@p11'])==D(a['postedQuantity']) and D(ip['@p10'])==D(a['unitCost']) and D(ip['@p5'])==D(fg['debit_amount']))
check('OP80 machine posting preceded actual final receipt accounting',len(accounting['actualOp80MachineSqlRecords'])==1 and accounting['actualOp80MachineSqlRecords'][0]['Timestamp']<record['Timestamp'])
cl=read('cleanup-manifest');check('own runtime Stopped cleanup0 and container volume network0',cl['state']=='Stopped' and cl['cleanup']['remaining']==[] and cl['cleanup']['errors']==[] and all(not raw[n].strip() for n in ['remaining-containers.txt','remaining-volumes.txt','remaining-networks.txt','remaining-labeled-networks.txt']))
check('own observer exited and exact password scan0',not read('observer-cleanup')['listening'] and read('password-value-scan')['hits']==0)
result={'runSha':sha,'checks':checks,'passed':sum(x['passed'] for x in checks),'failed':sum(not x['passed'] for x in checks),'archiveFactsVerified':all(x['passed'] for x in checks),'whole4366Accepted':False,'independentReviewRequired':True,'unverified':['public voucher detail','public GL WIP balance','independent DB SELECT'],'evidencePlane':'archive validation; no new runtime or repository gate','uiWriteCount':len(writes),'uiWriteStatuses':dict(collections.Counter(x['status'] for x in writes)),'formalRepositoryGates':'not run; no production changes and no completion candidate'}
Path(args.output).write_text(json.dumps(result,ensure_ascii=False,indent=2));print('Archive facts validated; acceptance awaits independent review',flush=True);sys.exit(0 if result['archiveFactsVerified'] else 1)
