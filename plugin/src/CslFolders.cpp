#include "CslFolders.h"

#include <algorithm>

namespace xpc {
namespace {

std::string Trim(const std::string& text) {
    const char* space = " \t\r\n";
    size_t start = text.find_first_not_of(space);
    if (start == std::string::npos) return {};
    size_t end = text.find_last_not_of(space);
    return text.substr(start, end - start + 1);
}

bool IsAbsolute(const std::string& path) {
    return (!path.empty() && (path[0] == '/' || path[0] == '\\')) || (path.size() > 1 && path[1] == ':');
}

std::string Native(std::string path) {
#ifdef _WIN32
    std::replace(path.begin(), path.end(), '/', '\\');
#else
    std::replace(path.begin(), path.end(), '\\', '/');
#endif
    while (path.size() > 1 && (path.back() == '/' || path.back() == '\\')) path.pop_back();
    return path;
}

}  // namespace

const std::vector<std::string>& KnownCslFolders() {
    static const std::vector<std::string> folders = {
        "Resources/plugins/XPConnect/Resources/CSL",
        "Custom Data/CSL",
        "Resources/plugins/LiveTraffic/Resources/CSL",
        "Resources/plugins/xPilot/Resources/CSL",
        "Resources/plugins/X-IvAp Resources/CSL",
        "Resources/plugins/swift/Resources/CSL",
    };
    return folders;
}

std::vector<std::string> CslFolders(const std::string& xplaneRoot,
                                    const std::vector<std::string>& extraLines,
                                    const std::function<bool(const std::string&)>& exists) {
    std::vector<std::string> candidates;
    for (const auto& line : extraLines) {
        std::string path = Trim(line);
        if (path.empty() || path[0] == '#') continue;
        candidates.push_back(Native(IsAbsolute(path) ? path : xplaneRoot + path));
    }
    for (const auto& relative : KnownCslFolders()) candidates.push_back(Native(xplaneRoot + relative));

    std::vector<std::string> found;
    for (const auto& path : candidates) {
        if (std::find(found.begin(), found.end(), path) == found.end() && exists(path)) found.push_back(path);
    }
    return found;
}

}  // namespace xpc
