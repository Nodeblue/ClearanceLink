// Minimal non-blocking UDP on 127.0.0.1, polled from X-Plane's flight loop.
#pragma once

#include <cstddef>
#include <cstdint>
#include <string>

namespace xpc {

class UdpSocket {
public:
    UdpSocket() = default;
    ~UdpSocket();
    UdpSocket(const UdpSocket&) = delete;
    UdpSocket& operator=(const UdpSocket&) = delete;

    // Binds 127.0.0.1:port. On failure returns false and says why.
    bool Open(uint16_t port, std::string& error);
    void Close();
    bool IsOpen() const;

    // Next waiting datagram, or 0 when none. Never blocks.
    size_t Receive(char* buffer, size_t size);

    bool Send(uint16_t port, const std::string& text);

private:
#ifdef _WIN32
    uintptr_t socket_ = ~uintptr_t(0);
#else
    int socket_ = -1;
#endif
};

}  // namespace xpc
