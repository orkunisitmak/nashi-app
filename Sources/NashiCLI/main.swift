import Foundation
import NashiCore

let status=NashiCommandLine.run()
// An interrupted job has already cleaned up; end by the same signal so a calling shell stops too.
if [SIGHUP,SIGINT,SIGTERM].contains(status-128) { signal(status-128,SIG_DFL);raise(status-128) }
exit(status)
