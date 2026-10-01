// Where to look for CSL model packages. See docs/protocol/plugin.md.
#pragma once

#include <functional>
#include <string>
#include <vector>

namespace xpc {

// Candidate folders in load order. `xplaneRoot` ends with a path separator (as XPLMGetSystemPath returns it);
// `extraLines` are the lines of csl_paths.txt. Only folders for which `exists` returns true are returned,
// each once.
std::vector<std::string> CslFolders(const std::string& xplaneRoot,
                                    const std::vector<std::string>& extraLines,
                                    const std::function<bool(const std::string&)>& exists);

// Folders other traffic plugins keep their CSL packages in, relative to the X-Plane folder.
const std::vector<std::string>& KnownCslFolders();

}  // namespace xpc
