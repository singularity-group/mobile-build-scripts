platform :ios, '**IOS_VERSION**'

use_modular_headers!

target 'UnityFramework' do
use_frameworks! :linkage => :static # :linkage will be used starting with cocoapods v1.9.0
# Adding GoogleUtilities to both targets fixes this build error: https://github.com/firebase/firebase-ios-sdk/issues/4087
# The error started happening when target 'NotificationServiceExtension' was added which depends on a firebase pod.
pod 'GoogleUtilities'
**UNITY_FRAMEWORK_TARGET_CONTENTS**
end

target 'Unity-iPhone' do
use_frameworks! :linkage => :static # :linkage will be used starting with cocoapods v1.9.0
# Adding GoogleUtilities to both targets fixes this build error: https://github.com/firebase/firebase-ios-sdk/issues/4087
# The error started happening when embedded target 'NotificationServiceExtension' needed to depend on a firebase pod.
pod 'GoogleUtilities'
**UNITY_APP_TARGET_CONTENTS**
end

#region duplicate-classes-workaround
# Solution is based on info from this comment:
# https://github.com/CocoaPods/CocoaPods/issues/5768#issuecomment-342914131

post_install do |installer|
  remove_static_framework_duplicate_linkage()
end


PROJECT_ROOT_DIR = File.dirname(File.expand_path(__FILE__))
PODS_DIR = File.join(PROJECT_ROOT_DIR, 'Pods')
PODS_TARGET_SUPPORT_FILES_DIR = File.join(PODS_DIR, 'Target Support Files')

# Assumes that Unity-iPhone target does not depend on any cocoapods.      
def remove_static_framework_duplicate_linkage()
  puts "Removing duplicate linkage of static frameworks"

  Dir.glob(File.join(PODS_TARGET_SUPPORT_FILES_DIR, "Pods-*")).each do |path|
    next if not path.end_with? 'Pods-Unity-iPhone'

    Dir.glob(File.join(path, "*.xcconfig")).each do |xcconfig|
      lines = File.readlines(xcconfig)

      if other_ldflags_index = lines.find_index { |l| l.start_with?('OTHER_LDFLAGS') }
        puts "update #{xcconfig}"
        # other_ldflags = lines[other_ldflags_index]
        lines[other_ldflags_index] = "OTHER_LDFLAGS = $(inherited)\n"

        File.open(xcconfig, 'w') do |fd|
          fd.write(lines.join)
        end
      end
    end

  end
end

#endregion duplicate-classes-workaround
