// One background thread that runs jobs in turn. X-Plane-independent so it can be tested.
#pragma once

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <deque>
#include <functional>
#include <mutex>
#include <thread>

namespace clink {

// Runs queued jobs one after another on a single thread, started with the first job. Reading 40 models' wheels at the
// first sync used to start 40 threads at once (one std::async each) next to X-Plane's own; now they wait their turn.
// Jobs get a cancel flag to check between steps, so Stop() does not wait long for a read in progress. Post and Stop
// are called from one thread (X-Plane's main thread); a job shares nothing with it except what it captures by value
// and the promise it reports through.
class Worker {
public:
    using Job = std::function<void(const std::atomic<bool>& cancel)>;

    Worker() = default;
    Worker(const Worker&) = delete;
    Worker& operator=(const Worker&) = delete;
    ~Worker() { Stop(); }

    // Queues `job`. False when the thread could not be started; the job is then dropped. Never throws.
    bool Post(Job job) noexcept;
    // Cancels the running job, drops queued ones (a promise they hold then reports std::future_errc::broken_promise),
    // and joins the thread. Jobs can be posted again afterwards. Never throws.
    void Stop() noexcept;
    // Jobs queued and not yet started (for tests).
    size_t Pending();

private:
    void Run();

    std::mutex mutex_;
    std::condition_variable wake_;
    std::deque<Job> jobs_;
    std::thread thread_;
    std::atomic<bool> cancel_{false};
    bool stopping_ = false;
};

}  // namespace clink
