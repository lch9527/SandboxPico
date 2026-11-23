import csv,json,sys,statistics
from pathlib import Path
p=Path(sys.argv[1]); rows=list(csv.DictReader(p.open()))
valid=[r for r in rows if r['warmup']=='0' and float(r['focused_fraction'])>=.99 and float(r['present_fraction'])>=.99 and int(r['frames'])>=10 and r['state'] in ('Build','Running','Success','Failed')]
def mean(rs,k):
    pairs=[(float(r[k]),int(r['frames'])) for r in rs if r.get(k)]
    return round(sum(v*w for v,w in pairs)/sum(w for v,w in pairs),3) if pairs else None
out={'source':str(p),'total_rows':len(rows),'valid_rows':len(valid),'scope':'Focused, worn, after 8-second warmup; editor data is not Quest evidence','levels':[]}
for n in range(1,6):
    for phase,states in [('observe',('Build',)),('drive',('Running',))]:
        rs=[r for r in valid if int(r['level'])==n and r['state'] in states]
        if not rs:continue
        seconds=sum(int(r['frames'])/float(r['fps']) for r in rs)
        hz=mean(rs,'display_hz'); frames=sum(int(r['frames']) for r in rs)
        out['levels'].append(dict(level=n,phase=phase,seconds=round(seconds,1),frames=frames,fps=round(frames/seconds,2),minimum_one_second_fps=round(min(float(r['fps']) for r in rs),2),worst_window_p95_ms=round(max(float(r['p95_frame_ms']) for r in rs),2),display_hz=hz,asw_modes=sorted(set(r['asw_mode'] for r in rs)),gpu_raw=mean(rs,'app_gpu_raw'),cpu_raw=mean(rs,'app_cpu_raw'),gpu_util_raw=mean(rs,'gpu_util_raw'),cpu_worst_util_raw=mean(rs,'cpu_worst_util_raw'),draw_calls=mean(rs,'draw_calls'),setpass_calls=mean(rs,'setpass_calls'),triangles=mean(rs,'triangles'),gc_bytes=mean(rs,'gc_bytes'),main_thread_wall_ms=(mean(rs,'main_thread_ns') or 0)/1e6,render_thread_wall_ms=(mean(rs,'render_thread_ns')/1e6 if mean(rs,'render_thread_ns') is not None else None)))
print(json.dumps(out,ensure_ascii=False,indent=2))
