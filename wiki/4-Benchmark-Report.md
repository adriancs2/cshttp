# The Benchmark Reports of Different Strategies to Handle the Listening of HTTP Requests That Shape the Architecture Decision for `cshttp.server`

> A Stress-Test & Architecture White Paper for `cshttp.server` 1.0

---

## 0. What is `cshttp.server`?

**`cshttp.server`** is a lightweight, zero-dependency, self-contained HTTP/1.1 web server written in pure C# 7.3 that compiles directly into any `.NET Framework 4.8` or `.NET 8 / .NET 10+` Console Application or Windows/Linux Service—requiring **no IIS, no Kestrel, no ASP.NET Core hosting pipeline, and zero external NuGet packages**. Built on top of `cshttp` (an RFC 9112 byte-level HTTP/1.1 wire parser with built-in request-smuggling and Slowloris guards) and `cshttp.web` (a drop-in `System.Web`-compatible `HttpContext.Current`, `HttpRequest`, `HttpResponse`, and `HostingEnvironment` surface), `cshttp.server` allows developers to port classic Pageless ASP.NET Web Forms applications or build brand-new standalone C# web applications with a simple `switch (path)` router and synchronous request handlers.

---

## 1. Executive Summary

When building a zero-dependency, standalone HTTP/1.1 web server in pure C#—capable of compiling cleanly on both classic **`.NET Framework 4.8`** and modern **`.NET 8 / .NET 10`** without external NuGet packages—the most consequential architectural decision lies at the boundary between the **TCP socket listener** and the **application request handler**.

Should a standalone C# web server use:
1. **One dedicated OS thread per TCP connection** with 100% synchronous blocking socket I/O (`cshttp.server.alpha.1.0`)?
2. **Asynchronous `TcpListener` + `Task.Run` + `async/await` stream I/O** wrapping a synchronous handler (the reference model from the *Zero-Framework C# Web Architecture* white paper)?
3. **A Hybrid Zero-Thread IOCP Idle State + Synchronous Worker Execution engine** (`cshttp.server` 1.0)?

To move beyond theoretical debate, we built a reproducible stress-test benchmark suite to push each TCP listening and connection-handling strategy to its breaking point and measure the exact throughput (Requests/sec), tail latency (`p99`), OS thread footprint, and memory consumption.

> **Reproducible Benchmark Source Code:**  
> All numbers and findings in this report were produced by the benchmark suite in [`benchmark-test`](https://github.com/adriancs2/cshttp/tree/main/tests/benchmark-test) *(see repository subfolder for `cshttp.Benchmark.csproj`, `Program.cs`, `Adapters.cs`, and `AlphaServerEngine.cs`)*.

---

## 2. The Benchmark Hardware & Why Constrained Cores Tell the Truth

All stress tests in this report were executed on the following machine:

* **CPU:** Intel® Core™ i7-4770S @ 3.10 GHz (**4 Physical Cores / 8 Logical Threads**)
* **Memory:** 24.0 GB DDR3 RAM
* **OS:** Windows (x64)
* **Runtimes Tested:** `.NET 10.0.12` (Release) and `.NET Framework 4.8.9294.0` (Release)

### Why a 4-Core / 8-Thread CPU is an Ideal Stress-Test Candidate

When benchmarking web server architectures on a 64-core server, hardware abundance frequently masks fundamental software design flaws. On a **4-core / 8-thread CPU**, physical execution cores are finite and unforgiving. Any architectural mistake immediately surfaces in the metrics:

1. **OS Thread Context-Switching Overhead:** If an architecture spawns 1,500 OS threads across 8 logical processors, Windows kernel scheduler thrashing and CPU cache eviction show up immediately in `p99` tail latency.
2. **IOCP vs. Worker Thread Starvation:** If synchronous blocking code accidentally executes on an I/O Completion Port (IOCP) callback thread with only 8 logical cores, network completion stalls across all other sockets within milliseconds.
3. **Ephemeral Port & ThreadPool Injection Delays:** If an architecture tears down TCP sockets per request or relies on naive `.NET ThreadPool` hill-climbing under blocking database loads, throughput collapses immediately.

---

## 3. The Three Contenders: Architectural Breakdown

All three server engines share the exact same RFC 9112 byte-level HTTP/1.1 parser (`cshttp`), the same `System.Web`-compatible `HttpContext.Current` (`AsyncLocal<HttpContext>`) context model (`cshttp.web`), and the same synchronous application handler (`Action<HttpContext>`). They differ **only** in how they accept TCP connections and schedule socket I/O:

| Architectural Dimension | Strategy 1: `cshttp.server.alpha.1.0` | Strategy 2: White Paper Model (`2a` & `2b`) | Strategy 3: Official `cshttp.server` 1.0 |
| :--- | :--- | :--- | :--- |
| **Listener Accept** | Dedicated `Thread` calling blocking `socket.Accept()` | `await listener.AcceptTcpClientAsync()` | Dedicated `Thread` calling blocking `socket.Accept()` |
| **Connection Dispatch** | **`new Thread(conn.Run).Start()`** (1 dedicated OS thread per TCP socket) | `Task.Run(async () => ...)` onto the `.NET ThreadPool` | **Zero threads on accept**; arms async OS IOCP read (`BeginRead`) |
| **Idle Keep-Alive State** | **Holds the 1 MB OS thread** blocked in `_stream.Read()` for up to 60s (`KeepAliveTimeout`) | `await stream.ReadAsync()` (0 threads while idle in `2b`; socket closed after 1st request in `2a`) | **Zero threads held while idle**; lightweight timer sweeps expired deadlines |
| **Active Request Execution** | 100% Synchronous (`Read` → `Parse` → `Handler` → `Flush`) on the connection's dedicated thread | `await ReadAsync` → Sync `Parse` + `Handler` → `await WriteAsync` (allocates `Task` state machines) | **IOCP callback hands off to ThreadPool Worker** → runs 100% synchronously + drains `_socket.Available > 0` |
| **Default Connection Cap** | `MaxConnections = 1,000` (hard wall to prevent OS thread exhaustion) | Limited by `ThreadPool` / RAM | `MaxConnections = 10,000` |

---

## 4. Deep-Dive Architectural Analysis: Before Looking at the Numbers

### 4.1 Why `cshttp.server.alpha.1.0` Chose `new Thread()` Over `ThreadPool`
At first glance, spawning `new Thread(conn.Run)` per connection in `cshttp.server.alpha.1.0` looks like an unorthodox choice compared to queuing work to the `.NET ThreadPool`. In reality, for a 100% synchronous blocking server that supports HTTP/1.1 Keep-Alive, **it was a necessary defensive measure**:

* Modern web browsers open **6 concurrent keep-alive TCP sockets per origin** and hold them open for 60 seconds even when the user is just reading a page.
* Because `Connection.Run()` blocks synchronously inside `_stream.Read()` while waiting for the next keep-alive request, queuing `Connection.Run()` onto the `.NET ThreadPool` would mean **just 2 browser tabs (12 idle connections) would consume all 8 `MinThreads` on an 8-thread CPU**.
* Once `ThreadPool` worker threads are blocked in synchronous socket reads, `.NET`'s hill-climbing algorithm injects only **1 to 2 threads per second**, freezing incoming requests. Spawning `new Thread(conn.Run)` bypassed `ThreadPool` starvation completely—at the cost of **~1 MB of OS stack space per open connection** and a hard `MaxConnections = 1000` ceiling.

### 4.2 The "Behind Nginx" vs. "Direct Browser Exposure" Paradox
* **Behind Nginx Reverse Proxy (`keepalive 32`):** When Nginx sits in front of the C# server, Nginx terminates thousands of public client connections and multiplexes active HTTP requests over a small pool of **32 persistent loopback sockets (`127.0.0.1`)**. In this topology, `cshttp.server.alpha.1.0` only ever creates 32 OS threads, pays zero thread-creation overhead per request, and performs admirably.
* **Direct Exposure (`--public` or LAN):** When browsers connect directly without Nginx, **167 users with an open browser tab (`167 × 6 = 1,002 sockets`) completely exhaust `MaxConnections = 1000`** in `alpha.1.0`. Even when the server is doing **0% CPU work**, the 168th user is immediately refused!

### 4.3 The IOCP Starvation Trap Discovered During `cshttp.server` 1.0 Testing
While designing `cshttp.server` 1.0, our first prototype replaced the blocking keep-alive wait with `_stream.BeginRead(...)` (OS IOCP) and executed the synchronous request handler directly inside the `EndRead` completion callback.

Our stress test immediately caught a critical flaw:
* When 128 concurrent clients hit an endpoint performing a **15 ms synchronous blocking database query**, running that synchronous handler directly on the thread that invoked `OnFirstReadComplete` **blocked the `.NET ThreadPool`'s I/O Completion Port (IOCP) threads**!
* With IOCP threads blocked inside SQL queries, the OS could no longer dispatch socket read completions for other connections, causing `p99` latency to spike to **3,389 ms** and throughput to drop by 60%.

**The `cshttp.server` 1.0 Solution:**
1. **Zero-Thread IOCP Idle Wait (`ArmIdleRead`):** When a connection has no unread bytes (`_start == _end`), it registers `_stream.BeginRead` and releases the thread completely. Idle connections consume **0 threads**.
2. **Strict IOCP-to-Worker Hand-Off (`CompleteAsyncFirstRead`):** The microsecond the first packet arrives on an IOCP thread, `EndRead` records the byte count and immediately calls `ThreadPool.QueueUserWorkItem(OnRunActiveWorkerCallback, this)`, freeing the IOCP thread in nanoseconds.
3. **Synchronous Worker Burst Loop (`RunActiveWorker`):** The ThreadPool Worker thread parses the head, reads the body, sets `HttpContext.Current`, invokes the synchronous `Action<HttpContext>` handler, flushes the response, and checks `if (_socket.Available > 0)`. If the client (or Nginx) has already sent another request on the keep-alive socket, the worker thread processes it immediately in a tight synchronous loop with **zero async state-machine allocations and zero context switches**. Only when `_socket.Available == 0` does the worker return the socket to the zero-thread IOCP idle state.

```
┌────────────────────────────────────────────────────────────────────────────┐
│                   cshttp.server 1.0 Connection Lifecycle                   │
└────────────────────────────────────────────────────────────────────────────┘

  [Socket Accepted / Keep-Alive Idle]
                  │
                  ▼
  ┌───────────────────────────────────────────────┐
  │ 1. Zero-Thread IOCP Idle State (ArmIdleRead)  │
  │    • _stream.BeginRead(_buf, ...)             │
  │    • 0 OS Threads held while waiting          │
  │    • Swept by lightweight 200ms deadline timer│
  └───────────────────────────────────────────────┘
                  │
                  │  First packet arrives (OS IOCP Interrupt)
                  ▼
  ┌───────────────────────────────────────────────┐
  │ 2. IOCP Completion (CompleteAsyncFirstRead)   │
  │    • _stream.EndRead(ar)                      │
  │    • QueueUserWorkItem(RunActiveWorker)       │
  │    • IOCP thread freed in nanoseconds!        │
  └───────────────────────────────────────────────┘
                  │
                  ▼
  ┌───────────────────────────────────────────────┐
  │ 3. Synchronous Worker Loop (RunActiveWorker)  │◄──────────────┐
  │    • Sync ParseHead + Body Read               │               │
  │    • HttpContext.Current = context            │               │
  │    • Sync Handler(context) (DB / File / HTML) │   _socket.Available > 0
  │    • Sync BufferedStream Flush                │   (Zero-switch keep-alive
  └───────────────────────────────────────────────┘    or pipelined burst!)
                  │                                               │
                  ├───────────────────────────────────────────────┘
                  │  _socket.Available == 0 (Socket is now idle)
                  ▼
        [Back to Step 1: 0 Threads]
```

---

## 5. Official Stress-Test Benchmark Results

The benchmark suite tests all strategies across **three distinct production workloads**, on both **`.NET 10.0`** and **`.NET Framework 4.8`**.

### Workload 1: Nginx Upstream Keep-Alive Pool (32 Persistent Connections, Pure Throughput)
*Simulates an Nginx reverse proxy multiplexing high-speed HTTP/1.1 traffic over 32 persistent loopback connections (`127.0.0.1`). Measures raw parser + dispatcher + socket I/O ceiling.*

#### `.NET 10.0` (Release)
| Strategy | Req / sec (RPS) | Avg Latency | p99 Latency | OS Threads Added | Socket Errors |
| :--- | ---: | ---: | ---: | ---: | ---: |
| **1. `cshttp.server.alpha.1.0`** (1 OS Thread/Conn, Sync I/O) | **50,849 /s** | 0.81 ms | 5.75 ms | +52 | 0 |
| **2a. Paper Model One-Shot** (`AcceptAsync` + `Task.Run` + `Dispose`) | **13,020 /s** | 2.45 ms | 12.62 ms | +0 | 0 |
| **2b. Paper Model + Keep-Alive** (`Task.Run` + `Async Read/Write`) | **81,620 /s** | 0.40 ms | 2.36 ms | +15 | 0 |
| **3. `cshttp.server` 1.0** (0-Thread IOCP Idle + Sync Worker) | **71,082 /s** | 0.46 ms | 3.79 ms | **+12** | 0 |

#### `.NET Framework 4.8` (Release)
| Strategy | Req / sec (RPS) | Avg Latency | p99 Latency | OS Threads Added | Socket Errors |
| :--- | ---: | ---: | ---: | ---: | ---: |
| **1. `cshttp.server.alpha.1.0`** (1 OS Thread/Conn, Sync I/O) | **44,570 /s** | 0.81 ms | 10.12 ms | +111 | 0 |
| **2a. Paper Model One-Shot** (`AcceptAsync` + `Task.Run` + `Dispose`) | **2,210 /s** ❌ | 13.71 ms | 45.32 ms | +0 | **1,257** ❌ |
| **2b. Paper Model + Keep-Alive** (`Task.Run` + `Async Read/Write`) | **43,307 /s** | 0.79 ms | 5.51 ms | +0 | 0 |
| **3. `cshttp.server` 1.0** (0-Thread IOCP Idle + Sync Worker) | **51,701 /s** 🏆 | **0.64 ms** 🏆 | 6.80 ms | +62 | 0 |

**Analysis of Workload 1:**
* **One-Shot Connections (`2a`) Collapse Under High RPS:** Closing the TCP socket after each request (`client.Dispose()`) drops throughput from **71k–81k RPS down to 13,020 RPS** on `.NET 10`, and causes **1,257 ephemeral port exhaustion errors (`2,210 RPS`)** on `.NET Framework 4.8`. Persistent HTTP/1.1 Keep-Alive is mandatory even over `127.0.0.1` loopback.
* **On `.NET Framework 4.8`, `cshttp.server` 1.0 is the fastest engine (`51,701 RPS`)**, beating `async/await` (`43,307 RPS`) by **+19%** because `.NET 4.8` lacks modern `ValueTask` / pooled `async` state machines, making synchronous `_socket.Available > 0` burst loops significantly cheaper.

---

### Workload 2: Blocking Database Burst (128 Concurrent Clients, 15ms Sync SQL Wait per Request)
*Simulates 128 concurrent connections simultaneously executing a synchronous 15 ms blocking database query (`Thread.Sleep(15)` inside the handler). Theoretical mathematical maximum for 128 clients with a 15 ms wait is $128 \times (1000 / 15) \approx 8,533\text{ RPS}$ across infinite cores, or ~4,200–4,400 RPS with context-switching and scheduling on 4 cores.*

#### `.NET 10.0` (Release)
| Strategy | Req / sec (RPS) | Avg Latency | p99 Latency | OS Threads Added | Errors |
| :--- | ---: | ---: | ---: | ---: | ---: |
| **1. `cshttp.server.alpha.1.0`** (1 OS Thread/Conn, Sync I/O) | **4,098 /s** | 32.76 ms | 45.93 ms | +2 | 0 |
| **2b. Paper Model + Keep-Alive** (`Task.Run` + `Async Read/Write`) | **4,366 /s** | 29.68 ms | 44.49 ms | +73 | 0 |
| **3. `cshttp.server` 1.0** (0-Thread IOCP Idle + Sync Worker) | **4,423 /s** 🏆 | **29.71 ms** | **41.13 ms** 🏆 | **+20** | 0 |

#### `.NET Framework 4.8` (Release)
| Strategy | Req / sec (RPS) | Avg Latency | p99 Latency | OS Threads Added | Errors |
| :--- | ---: | ---: | ---: | ---: | ---: |
| **1. `cshttp.server.alpha.1.0`** (1 OS Thread/Conn, Sync I/O) | **4,233 /s** | 32.55 ms | 68.04 ms | +2 | 0 |
| **2b. Paper Model + Keep-Alive** (`Task.Run` + `Async Read/Write`) | **4,030 /s** | 33.61 ms | 57.91 ms | +69 | 0 |
| **3. `cshttp.server` 1.0** (0-Thread IOCP Idle + Sync Worker) | **4,324 /s** 🏆 | **31.51 ms** 🏆 | 58.93 ms | +148 | 0 |

**Analysis of Workload 2:**
* By pre-warming `ThreadPool.SetMinThreads` in `HttpServer.Start()` and immediately handing off from the IOCP completion callback to a `ThreadPool` worker thread, **`cshttp.server` 1.0 achieves the highest throughput on both `.NET 10` (`4,423 RPS`) and `.NET 4.8` (`4,324 RPS`)** when handlers block synchronously on database or file I/O.

---

### Workload 3: The "C2000" Direct Browser Flood (1,500 Idle Keep-Alive Sockets + 50 Active Workers)
*Simulates 250 users with an open browser tab (`250 × 6 = 1,500` concurrent idle keep-alive sockets) while 50 active clients continuously hammer the server.*

#### `.NET 10.0` (Release)
| Strategy | Idle Conns Accepted | Active Req / sec | p99 Latency | OS Threads Added | RAM Added |
| :--- | ---: | ---: | ---: | ---: | ---: |
| **1a. `cshttp.server.alpha.1.0`** (Default `MaxConn=1000`) | **1,000 / 1,500** ❌ | **0 /s (REFUSED)** ❌ | — | **+1,001** | +108.0 MB |
| **1b. `cshttp.server.alpha.1.0`** (Raised `MaxConn=2000`) | 1,500 / 1,500 | **12,997 /s** | 36.10 ms | **+1,500** | +138.5 MB |
| **2b. Paper Model + Keep-Alive** (`Task.Run` + `Async I/O`) | 1,500 / 1,500 | **31,758 /s** | 11.12 ms | **+0** | ~0.0 MB |
| **3. `cshttp.server` 1.0** (0-Thread IOCP Idle + Sync Worker) | **1,500 / 1,500** ✅ | **62,031 /s** 🏆 | **6.32 ms** 🏆 | **+1** 🏆 | +104.8 MB |

#### `.NET Framework 4.8` (Release)
| Strategy | Idle Conns Accepted | Active Req / sec | p99 Latency | OS Threads Added | RAM Added |
| :--- | ---: | ---: | ---: | ---: | ---: |
| **1a. `cshttp.server.alpha.1.0`** (Default `MaxConn=1000`) | **1,000 / 1,500** ❌ | **0 /s (REFUSED)** ❌ | — | **+1,000** | +120.7 MB |
| **1b. `cshttp.server.alpha.1.0`** (Raised `MaxConn=2000`) | 1,500 / 1,500 | **7,444 /s** ❌ | **738.43 ms** ❌ | **+1,501** | +183.8 MB |
| **2b. Paper Model + Keep-Alive** (`Task.Run` + `Async I/O`) | 1,500 / 1,500 | **32,546 /s** | 12.55 ms | **+0** | +31.2 MB |
| **3. `cshttp.server` 1.0** (0-Thread IOCP Idle + Sync Worker) | **1,500 / 1,500** ✅ | **49,966 /s** 🏆 | **7.19 ms** 🏆 | **+1** 🏆 | +102.2 MB |

**Analysis of Workload 3 (The Decisive Test):**
1. **`cshttp.server.alpha.1.0` (`MaxConn=1000`) Hits Total Denial of Service (`0 RPS`):** Once 1,000 idle keep-alive sockets connect, every single slot in `alpha.1.0` is occupied by an idle thread blocked in `_stream.Read()`. When the 50 active clients attempt to connect, 100% of their connections are immediately rejected.
2. **Raising `MaxConn=2000` in `alpha.1.0` Causes Severe OS Thread Thrashing:** Forcing `alpha.1.0` to accept all 1,500 idle sockets spawns **+1,500 OS threads**. On `.NET Framework 4.8`, scheduling across 1,550 threads causes `p99` tail latency to explode to **738.43 ms** (nearly three-quarters of a second for a 2-byte `"OK"` response!) and collapses throughput to **7,444 RPS**.
3. **`cshttp.server` 1.0 Dominates Both `alpha.1.0` and Pure `async/await`:**
   * Holding 1,500 idle keep-alive connections in `cshttp.server` 1.0 adds **only +1 OS thread**.
   * While those 1,500 idle sockets sit open, the 50 active clients achieve **62,031 RPS (`6.32 ms` p99)** on `.NET 10` and **49,966 RPS (`7.19 ms` p99)** on `.NET Framework 4.8`.
   * That is **4.8× to 6.7× faster than `alpha.1.0`**, and **1.5× to 2.0× faster than the `async/await` Paper Model (`31,758 RPS`)**, because active keep-alive connections in `cshttp.server` 1.0 execute straight through on hot worker threads without allocating `Task` state machines on every read and write.

---

## 6. Conclusion: Why `cshttp.server` 1.0 is Built This Way

The benchmark data proves that a C# web server does **not** need `async/await` infecting application handlers—nor does it need external NuGet dependencies like `System.IO.Pipelines` or `System.Threading.Channels`—to scale to tens of thousands of concurrent connections and deliver 60,000+ requests per second on a modest 4-core CPU.

By separating the **connection lifecycle** into two distinct phases inside `cshttp.server` 1.0:
1. **Asynchronous, Zero-Thread OS IOCP Wait (`BeginRead`)** whenever a socket is waiting for the first byte of a request (newly accepted or idle between keep-alive requests), and
2. **100% Synchronous Worker Execution** once bytes arrive in kernel memory (including zero-switch `_socket.Available > 0` keep-alive burst draining),

`cshttp.server` 1.0 gives C# developers the ergonomics of simple, step-through-debuggable synchronous request handlers (`Action<HttpContext>`) with the idle-connection scalability of an asynchronous event-loop server—running identically on `.NET Framework 4.8` and `.NET 8/10+`.
