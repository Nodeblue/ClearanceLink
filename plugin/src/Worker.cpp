#include "Worker.h"

namespace clink {

bool Worker::Post(Job job) noexcept {
    try {
        std::lock_guard<std::mutex> lock(mutex_);
        if (!thread_.joinable()) {
            cancel_ = false;
            stopping_ = false;
            thread_ = std::thread(&Worker::Run, this);
        }
        jobs_.push_back(std::move(job));
    } catch (...) {  // std::system_error (no thread) or std::bad_alloc
        return false;
    }
    wake_.notify_one();
    return true;
}

void Worker::Stop() noexcept {
    std::deque<Job> dropped;
    {
        std::lock_guard<std::mutex> lock(mutex_);
        stopping_ = true;
        cancel_ = true;
        dropped.swap(jobs_);
    }
    wake_.notify_all();
    if (thread_.joinable()) {
        try {
            thread_.join();
        } catch (...) {  // only when a job stops its own worker, which none does
        }
    }
    dropped.clear();  // outside the lock: destroying a job breaks its promise
    std::lock_guard<std::mutex> lock(mutex_);
    stopping_ = false;
    cancel_ = false;
}

size_t Worker::Pending() {
    std::lock_guard<std::mutex> lock(mutex_);
    return jobs_.size();
}

void Worker::Run() {
    for (;;) {
        Job job;
        {
            std::unique_lock<std::mutex> lock(mutex_);
            wake_.wait(lock, [this] { return stopping_ || !jobs_.empty(); });
            if (stopping_) return;
            job = std::move(jobs_.front());
            jobs_.pop_front();
        }
        try {
            job(cancel_);
        } catch (...) {  // a job reports its own errors; an exception out of a thread ends the process
        }
    }
}

}  // namespace clink
