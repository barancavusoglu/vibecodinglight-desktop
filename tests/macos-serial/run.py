import os, pty, subprocess, sys, threading, queue
master, slave = pty.openpty()
address = os.ttyname(slave)
os.close(slave)
process = subprocess.Popen([sys.argv[1], sys.argv[2], address], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
lines = queue.Queue()
def read():
    for line in process.stdout:
        lines.put(line.strip())
threading.Thread(target=read, daemon=True).start()
try:
    while True:
        line = lines.get(timeout=10)
        print(line, flush=True)
        if line == 'CLOSE_PEER':
            os.close(master)
            master = None
            process.stdin.write('\n')
            process.stdin.flush()
            break
    code = process.wait(timeout=5)
    while not lines.empty(): print(lines.get(), flush=True)
    if code: print(process.stderr.read(), file=sys.stderr)
    assert code == 0
    print('PASS real macOS pseudo-terminal queued-write disconnect closes the shared serial link within one second')
finally:
    if process.poll() is None: process.kill(); process.wait()
    if master is not None: os.close(master)
