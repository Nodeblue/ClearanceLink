#include "Udp.h"

#ifdef _WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
#else
#include <arpa/inet.h>
#include <cerrno>
#include <cstring>
#include <fcntl.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
#endif

namespace xpc {
namespace {

#ifdef _WIN32
using Native = SOCKET;
constexpr Native Invalid = INVALID_SOCKET;
std::string LastError() { return "WSA error " + std::to_string(WSAGetLastError()); }
#else
using Native = int;
constexpr Native Invalid = -1;
std::string LastError() { return std::strerror(errno); }
#endif

sockaddr_in Loopback(uint16_t port) {
    sockaddr_in address{};
    address.sin_family = AF_INET;
    address.sin_port = htons(port);
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    return address;
}

}  // namespace

UdpSocket::~UdpSocket() { Close(); }

bool UdpSocket::IsOpen() const { return static_cast<Native>(socket_) != Invalid; }

bool UdpSocket::Open(uint16_t port, std::string& error) {
    Close();
#ifdef _WIN32
    WSADATA data;
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0) {
        error = "WSAStartup failed";
        return false;
    }
#endif
    Native s = ::socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (s == Invalid) {
        error = "socket: " + LastError();
        return false;
    }
    socket_ = s;
    // Room for a few seconds of XPConnect's updates (64 aircraft is about 75 KB a second): the socket is only read
    // once a frame, and X-Plane stalls for a second or more loading scenery. The default is 64 KB on Windows.
    int receiveBuffer = 4 * 1024 * 1024;
    ::setsockopt(s, SOL_SOCKET, SO_RCVBUF, reinterpret_cast<const char*>(&receiveBuffer), sizeof(receiveBuffer));
    sockaddr_in address = Loopback(port);
    if (::bind(s, reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0) {
        error = "bind 127.0.0.1:" + std::to_string(port) + ": " + LastError();
        Close();
        return false;
    }
#ifdef _WIN32
    u_long nonBlocking = 1;
    ioctlsocket(s, FIONBIO, &nonBlocking);
    // Without this, a send to a closed port (XPConnect not running) makes the next recv fail with WSAECONNRESET.
    BOOL reportReset = FALSE;
    DWORD returned = 0;
    WSAIoctl(s, _WSAIOW(IOC_VENDOR, 12), &reportReset, sizeof(reportReset), nullptr, 0, &returned, nullptr, nullptr);
#else
    fcntl(s, F_SETFL, fcntl(s, F_GETFL, 0) | O_NONBLOCK);
#endif
    return true;
}

void UdpSocket::Close() {
    if (!IsOpen()) return;
#ifdef _WIN32
    closesocket(static_cast<Native>(socket_));
    WSACleanup();
#else
    ::close(socket_);
#endif
    socket_ = Invalid;
}

size_t UdpSocket::Receive(char* buffer, size_t size) {
    if (!IsOpen()) return 0;
#ifdef _WIN32
    int n = ::recv(static_cast<Native>(socket_), buffer, static_cast<int>(size), 0);
#else
    ssize_t n = ::recv(socket_, buffer, size, 0);
#endif
    return n > 0 ? static_cast<size_t>(n) : 0;
}

bool UdpSocket::Send(uint16_t port, const std::string& text) {
    if (!IsOpen()) return false;
    sockaddr_in address = Loopback(port);
#ifdef _WIN32
    int n = ::sendto(static_cast<Native>(socket_), text.data(), static_cast<int>(text.size()), 0,
                     reinterpret_cast<sockaddr*>(&address), sizeof(address));
#else
    ssize_t n = ::sendto(socket_, text.data(), text.size(), 0, reinterpret_cast<sockaddr*>(&address), sizeof(address));
#endif
    return n == static_cast<decltype(n)>(text.size());
}

}  // namespace xpc
