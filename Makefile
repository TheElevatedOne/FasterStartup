# Build, install and package FasterStartup.
#
#   make build                 Build the plugin and the patcher (bin/Release)
#   make install               Build and copy into an r2modman profile
#   make uninstall             Remove from that profile
#   make publish               Build the Thunderstore zip into dist/ (see publish.sh); upload it by hand
#   make package               Same as make publish
#   make clean                 Remove build output
#
# Paths can be overridden, e.g.:
#   make install PROFILE_DIR="$HOME/.config/r2modmanPlus-local/RiskOfRain2/profiles/Hallowed Ready"
RORDIR      ?= $(HOME)/.local/share/Steam/steamapps/common/Risk of Rain 2
PROFILE_DIR ?= $(HOME)/.config/r2modmanPlus-local/RiskOfRain2/profiles/Default
CONFIG      ?= Release

# Package identity and version come from thunderstore.conf.
NAMESPACE := $(shell bash -c 'source ./thunderstore.conf && echo $$NAMESPACE')
NAME      := $(shell bash -c 'source ./thunderstore.conf && echo $$NAME')
VERSION   := $(shell bash -c 'source ./thunderstore.conf && echo $$VERSION')

PLUGIN_DLL  := bin/$(CONFIG)/$(NAME).dll
PATCHER_DLL := bin/$(CONFIG)/$(NAME).Patcher.dll
PLUGIN_DIR  := $(PROFILE_DIR)/BepInEx/plugins/$(NAMESPACE)-$(NAME)
PATCHER_DIR := $(PROFILE_DIR)/BepInEx/patchers/$(NAMESPACE)-$(NAME)

.PHONY: all build install uninstall publish package clean

all: build

build:
	dotnet build src/Plugin/$(NAME).csproj -c $(CONFIG) -p:Version=$(VERSION) -p:RoR2Dir="$(RORDIR)" -p:ProfileDir="$(PROFILE_DIR)"

install: build
	mkdir -p "$(PLUGIN_DIR)" "$(PATCHER_DIR)"
	cp "$(PLUGIN_DLL)" icon.png "$(PLUGIN_DIR)/"
	cp "$(PATCHER_DLL)" "$(PATCHER_DIR)/"
	@echo "Installed $(NAME) $(VERSION) to $(PROFILE_DIR)"

uninstall:
	rm -rf "$(PLUGIN_DIR)" "$(PATCHER_DIR)"

publish: build
	./publish.sh "$(PLUGIN_DLL)" "$(PATCHER_DLL)"

package: publish

clean:
	-dotnet clean src/Plugin/$(NAME).csproj -c $(CONFIG) >/dev/null
	rm -rf bin obj src/*/obj dist
