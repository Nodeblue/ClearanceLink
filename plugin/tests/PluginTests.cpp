// Tests for the parts of the plugin that need no X-Plane: the protocol and the CSL folder search.
#include "CslFolders.h"
#include "Protocol.h"

#include <cmath>
#include <cstdio>
#include <cstring>
#include <set>
#include <string>

namespace {

int g_failures = 0;

void Check(bool ok, const char* what, int line) {
    if (!ok) {
        std::printf("FAIL line %d: %s\n", line, what);
        ++g_failures;
    }
}

#define CHECK(x) Check((x), #x, __LINE__)

void ParsesPos() {
    auto m = xpc::Parse("pos 7 -27.384 153.117 23.5 13.0 2.5 -1.25 15.5 1 1 0.25 0 0.3 19");
    CHECK(m.has_value());
    auto pos = std::get<xpc::Pos>(*m);
    CHECK(pos.id == 7);
    CHECK(std::fabs(pos.lat + 27.384) < 1e-9);
    CHECK(std::fabs(pos.lon - 153.117) < 1e-9);
    CHECK(pos.altFt == 23.5);
    CHECK(pos.bank == -1.25f);
    CHECK(pos.onGround);
    CHECK(pos.flaps == 0.25f);
    CHECK(pos.lights == (xpc::LightNav | xpc::LightBeacon | xpc::LightTaxi));
}

void ParsesOffset() {
    auto offset = std::get<xpc::Offset>(*xpc::Parse("offset 9 -1.5"));
    CHECK(offset.id == 9);
    CHECK(offset.groundFt == -1.5f);
    std::string error;
    CHECK(!xpc::Parse("offset 9 900", &error) && error.find("bad offset") != std::string::npos);
}

void ParsesAddWithLabelAndUnknowns() {
    auto add = std::get<xpc::Add>(*xpc::Parse("add 3 B738 QFA - VH-XZE Qantas 1"));
    CHECK(add.id == 3);
    CHECK(add.icaoType == "B738");
    CHECK(add.icaoAirline == "QFA");
    CHECK(add.livery.empty());
    CHECK(add.label == "VH-XZE Qantas 1");
    auto bare = std::get<xpc::Add>(*xpc::Parse("add 4 C172 - -"));
    CHECK(bare.icaoAirline.empty() && bare.label.empty());
}

void RejectsBadLines() {
    std::string error;
    CHECK(!xpc::Parse("pos 7 1 2", &error) && error.find("14 fields") != std::string::npos);
    CHECK(!xpc::Parse("pos 0 1 2 3 4 5 6 7 0 0 0 0 0 0", &error));  // id 0 is not allowed
    CHECK(!xpc::Parse("pos 1 95 2 3 4 5 6 7 0 0 0 0 0 0", &error));  // latitude out of range
    CHECK(!xpc::Parse("pos 1 1,5 2 3 4 5 6 7 0 0 0 0 0 0", &error)); // decimal comma
    CHECK(!xpc::Parse("add 1 - QFA -", &error));
    CHECK(!xpc::Parse("warp 1", &error) && error == "unknown message: warp");
    CHECK(!xpc::Parse("   ", &error) && error.empty());
    CHECK(std::holds_alternative<xpc::Clear>(*xpc::Parse("clear")));
    CHECK(std::get<xpc::Del>(*xpc::Parse("del 9")).id == 9);
    CHECK(std::get<xpc::Hello>(*xpc::Parse("hello 0.4.0")).version == "0.4.0");
}

void SplitsDatagrams() {
    const char data[] = "hello 1\r\npos 1 0 0 0 0 0 0 0 0 0 0 0 0 0\n\nclear";
    auto lines = xpc::Lines(data, std::strlen(data));
    CHECK(lines.size() == 3);
    CHECK(lines[0] == "hello 1");
    CHECK(lines[2] == "clear");
}

void FormatsReplies() {
    CHECK(xpc::FormatGround(5, 12.345) == "ground 5 12.3");
    CHECK(xpc::FormatStatus("0.1.0", 2, 0, {}) == "status 0.1.0 2 0 -");
    CHECK(xpc::FormatStatus("0.1.0", 0, 900, {"C:\\a", "D:\\b c"}) == "status 0.1.0 0 900 C:\\a;D:\\b c");
    CHECK(xpc::FormatError(5, "no\nmodel") == "error 5 no model");
}

void FindsCslFolders() {
    std::set<std::string> existing;
#ifdef _WIN32
    const std::string root = "C:\\X-Plane 12\\";
    existing = {"D:\\CSL\\Bluebell", "C:\\X-Plane 12\\Resources\\plugins\\LiveTraffic\\Resources\\CSL"};
#else
    const std::string root = "/xp/";
    existing = {"/csl/Bluebell", "/xp/Resources/plugins/LiveTraffic/Resources/CSL", "/xp/Custom Data/CSL"};
#endif
    auto exists = [&](const std::string& p) { return existing.count(p) > 0; };
#ifdef _WIN32
    auto found = xpc::CslFolders(root, {"# comment", " D:\\CSL\\Bluebell ", "", "D:\\missing"}, exists);
    CHECK(found.size() == 2 && found[0] == "D:\\CSL\\Bluebell");
#else
    auto found = xpc::CslFolders(root, {"# comment", " /csl/Bluebell/ ", "", "/missing", "Custom Data/CSL"}, exists);
    CHECK(found.size() == 3);
    CHECK(found[0] == "/csl/Bluebell");
    CHECK(found[1] == "/xp/Custom Data/CSL");  // listed once although also a default
    CHECK(found[2] == "/xp/Resources/plugins/LiveTraffic/Resources/CSL");
#endif
}

}  // namespace

int main() {
    ParsesPos();
    ParsesOffset();
    ParsesAddWithLabelAndUnknowns();
    RejectsBadLines();
    SplitsDatagrams();
    FormatsReplies();
    FindsCslFolders();
    std::printf(g_failures == 0 ? "All plugin tests passed\n" : "%d failures\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
