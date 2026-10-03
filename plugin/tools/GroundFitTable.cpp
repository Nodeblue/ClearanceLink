// Prints how the plugin fits each CSL model to the ground, as a CSV table, so the fit can be checked against a whole
// CSL library without X-Plane (docs/protocol/plugin.md). Usage: xpconnect-groundfit <CSL folder>... > table.csv
// Reads the models only; writes nothing but the table.
#include "GroundFit.h"

#include <cstdio>
#include <filesystem>
#include <fstream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

namespace {

std::vector<std::string> Tokens(const std::string& line) {
    std::istringstream in(line);
    std::vector<std::string> tokens;
    for (std::string t; in >> t;) tokens.push_back(t);
    return tokens;
}

std::string Csv(std::string text) {
    for (auto& c : text)
        if (c == ',' || c == '"') c = ' ';
    return text;
}

}  // namespace

int main(int argc, char** argv) {
    if (argc < 2) {
        std::fprintf(stderr, "usage: xpconnect-groundfit <CSL folder>...\n");
        return 2;
    }
    const std::vector<std::string> folders(argv + 1, argv + argc);
    const auto packages = xpc::FindCslPackages(folders);
    std::fprintf(stderr, "%zu CSL packages\n", packages.size());

    std::printf("package,model,icao,vert_offset_m,obj_files,points,gear_points,bogie_dataref,bogie_level,bogie_tilt_deg,fit,nose_above_mains_m,"
                "pitch_deg,wheels_below_origin_m,why\n");
    // Liveries of one model share their .obj files; read each set once.
    std::map<std::vector<std::string>, std::pair<xpc::PosedModel, std::optional<xpc::Wheels>>> read;
    std::map<std::vector<std::string>, std::string> reasons;
    int models = 0, fitted = 0;

    // A package split over several folders has the same xsb_aircraft.txt in each: read it once.
    for (auto it = packages.begin(); it != packages.end(); it = packages.upper_bound(it->first)) {
        const auto& [package, folder] = *it;
        std::ifstream xsb(std::filesystem::u8path(folder) / "xsb_aircraft.txt");
        std::string line, model, icao, offset;
        int start = 0;
        auto flush = [&]() {
            if (start == 0) return;
            const auto files = xpc::ModelObjFiles(folder, start, packages);
            auto found = read.find(files);
            if (found == read.end()) {
                xpc::PosedModel posed = xpc::PoseObjFiles(files);
                std::string why;
                auto wheels = xpc::FindWheels(posed, &why);
                found = read.emplace(files, std::make_pair(std::move(posed), wheels)).first;
                reasons[files] = files.empty() ? "no .obj file found" : why;
            }
            const auto& [posed, wheels] = found->second;
            ++models;
            if (wheels) {
                ++fitted;
                std::printf("%s,%s,%s,%s,%zu,%zu,%zu,%s,%.3f,%.1f,yes,%.3f,%.2f,%.3f,\n", Csv(package).c_str(), Csv(model).c_str(),
                            Csv(icao).c_str(), offset.c_str(), files.size(), posed.all.size(), posed.gear.size(),
                            Csv(posed.bogieDataref).c_str(), posed.bogieLevel, posed.bogieTiltDeg,
                            wheels->noseY - wheels->mainY, wheels->LevelPitchDeg(),
                            wheels->GroundHeightM(wheels->LevelPitchDeg()));
            } else {
                std::printf("%s,%s,%s,%s,%zu,%zu,%zu,%s,%.3f,%.1f,no,,,,%s\n", Csv(package).c_str(), Csv(model).c_str(),
                            Csv(icao).c_str(), offset.c_str(), files.size(), posed.all.size(), posed.gear.size(),
                            Csv(posed.bogieDataref).c_str(), posed.bogieLevel, posed.bogieTiltDeg,
                            Csv(reasons[files]).c_str());
            }
        };
        for (int n = 1; std::getline(xsb, line); ++n) {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            const auto tokens = Tokens(line);
            if (tokens.empty()) continue;
            if (tokens[0] == "OBJ8_AIRCRAFT") {
                flush();
                start = n;
                model = tokens.size() > 1 ? tokens[1] : "";
                icao.clear();
                offset.clear();
            } else if ((tokens[0] == "ICAO" || tokens[0] == "MATCHES") && tokens.size() > 1 && icao.empty()) {
                icao = tokens[1];
            } else if (tokens[0] == "VERT_OFFSET" && tokens.size() > 1) {
                offset = tokens[1];
            }
        }
        flush();
    }
    std::fprintf(stderr, "%d models, %d fitted, %zu distinct .obj sets\n", models, fitted, read.size());
    return 0;
}
