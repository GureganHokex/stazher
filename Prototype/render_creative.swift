// Reproducible, original 15-second IGS motion creative. macOS, no third-party packages.
import AVFoundation
import CoreGraphics
import CoreText
import ImageIO
import Foundation

let output = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "IGS-demo-15s.mp4")
let width = 1280, height = 720, fps = 30, count = 450
let mint = CGColor(red: 0.40, green: 0.91, blue: 0.76, alpha: 1)
let white = CGColor(red: 0.95, green: 0.98, blue: 1, alpha: 1)
let grey = CGColor(red: 0.54, green: 0.66, blue: 0.73, alpha: 1)
func smooth(_ value: Double) -> Double { let x = max(0,min(1,value));return x*x*(3-2*x) }
func text(_ c: CGContext, _ value: String, _ x: CGFloat, _ y: CGFloat, _ size: CGFloat, _ color: CGColor = white, _ bold: Bool = false) {
    let font = CTFontCreateWithName((bold ? "HelveticaNeue-Bold" : "HelveticaNeue") as CFString, size, nil)
    let a = NSAttributedString(string: value, attributes: [NSAttributedString.Key(kCTFontAttributeName as String): font, NSAttributedString.Key(kCTForegroundColorAttributeName as String): color])
    c.textMatrix = .identity;c.textPosition = CGPoint(x:x,y:y);CTLineDraw(CTLineCreateWithAttributedString(a),c)
}
func line(_ c: CGContext, _ points: [CGPoint], _ alpha: CGFloat, _ thickness: CGFloat = 1) {
    c.setStrokeColor(mint.copy(alpha: alpha)!);c.setLineWidth(thickness);c.addLines(between:points);c.strokePath()
}
func render(_ c: CGContext, _ t: Double) {
    c.setFillColor(CGColor(red:0.025,green:0.055,blue:0.10,alpha:1));c.fill(CGRect(x:0,y:0,width:width,height:height))
    let colors = [CGColor(red:0.02,green:0.16,blue:0.20,alpha:1),CGColor(red:0.025,green:0.055,blue:0.10,alpha:1)]
    let gradient = CGGradient(colorsSpace:CGColorSpaceCreateDeviceRGB(),colors:colors as CFArray,locations:[0,1])!
    c.drawRadialGradient(gradient,startCenter:CGPoint(x:1050,y:390),startRadius:20,endCenter:CGPoint(x:1050,y:390),endRadius:670,options:[])
    // Moving perspective grid and floating surfaces make the artwork a motion creative.
    for i in 0...14 {
        let x = CGFloat(i)*125-240
        line(c,[CGPoint(x:910,y:370),CGPoint(x:x,y:0)],0.10)
    }
    for i in 0...9 {
        let p = (Double(i)/9+t*0.045).truncatingRemainder(dividingBy:1)
        let y = 330-p*p*330
        line(c,[CGPoint(x:650,y:y),CGPoint(x:1280,y:y)],0.08+0.10*p)
    }
    let orbit = sin(t*0.55)*15
    for index in (0...4).reversed() {
        let d = Double(index), x = 874+d*33+orbit, y = 235+d*19+sin(t*0.8+d)*7
        let w = 265-d*18, h = 190-d*13
        let pts = [CGPoint(x:x,y:y),CGPoint(x:x+w,y:y+28),CGPoint(x:x+w,y:y+h+28),CGPoint(x:x,y:y+h),CGPoint(x:x,y:y)]
        if index == 0 {
            c.setFillColor(CGColor(red:0.025,green:0.13,blue:0.17,alpha:1));c.addLines(between:pts);c.fillPath()
            text(c,"IGS",x+43,y+72,70,mint,true)
        }
        line(c,pts,index==0 ? 0.90 : 0.12+0.09*(4-d),index==0 ? 2.5 : 1)
    }
    text(c,"IGS  /  IN GAME SURFACE",76,649,16,mint,true)
    text(c,"ДЕМО-КРЕАТИВ  /  01",76,49,13,grey)
    text(c,"15 SEC     ·     IN-GAME VIDEO",933,49,12,grey)
    line(c,[CGPoint(x:76,y:610),CGPoint(x:1204,y:610)],0.18)
    c.setFillColor(mint);c.fill(CGRect(x:76,y:598,width:1128*CGFloat(t/15),height:2))
    let phase: Int = t<3.5 ? 0 : t<7.5 ? 1 : t<11 ? 2 : 3
    let start = [0.0,3.5,7.5,11.0][phase]
    let end = [3.5,7.5,11.0,15.0][phase]
    let opacity = smooth((t-start)/0.45) * smooth((end-t)/0.30)
    c.saveGState();c.setAlpha(opacity);c.translateBy(x:-20*(1-smooth((t-start)/0.65)),y:0)
    if phase == 0 {
        text(c,"IGS",68,345,172,white,true)
        text(c,"IN GAME SURFACE",77,292,33,mint,true)
        text(c,"Реклама внутри игрового мира",78,221,25)
    } else if phase == 1 {
        text(c,"ВНУТРИ ИГРЫ.",75,395,62,white,true)
        text(c,"В ПОЛЕ ЗРЕНИЯ.",75,316,59,mint,true)
        text(c,"Бренд становится частью окружения",78,229,25)
    } else if phase == 2 {
        text(c,"ИГРА",75,407,77,white,true)
        text(c,"ПРОДОЛЖАЕТСЯ",75,326,55,mint,true)
        text(c,"Новый формат на знакомых поверхностях",78,237,23)
        text(c,"Экраны  /  витрины  /  городские щиты",78,195,20,grey)
    } else {
        text(c,"IGS",68,372,150,white,true)
        text(c,"IN GAME SURFACE",77,317,33,mint,true)
        text(c,"РЕКЛАМА ВНУТРИ",78,234,30,white,true)
        text(c,"ИГРОВОГО МИРА",78,192,30,white,true)
    }
    c.restoreGState()
}

// --raw streams BGRA frames to FFmpeg when a system H.264 encoder is unavailable.
if CommandLine.arguments.contains("--raw") {
    for frame in 0..<count {
        autoreleasepool {
            let ctx=CGContext(data:nil,width:width,height:height,bitsPerComponent:8,bytesPerRow:width*4,space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedFirst.rawValue|CGBitmapInfo.byteOrder32Little.rawValue)!
            render(ctx,Double(frame)/Double(fps))
            FileHandle.standardOutput.write(Data(bytes:ctx.data!,count:width*height*4))
        }
    }
    exit(0)
}
try FileManager.default.createDirectory(at:output.deletingLastPathComponent(),withIntermediateDirectories:true)
if FileManager.default.fileExists(atPath:output.path) { try FileManager.default.removeItem(at:output) }
let writer = try AVAssetWriter(outputURL:output,fileType:.mp4)
let input = AVAssetWriterInput(mediaType:.video,outputSettings:[AVVideoCodecKey:AVVideoCodecType.h264,AVVideoWidthKey:width,AVVideoHeightKey:height,AVVideoCompressionPropertiesKey:[AVVideoAverageBitRateKey:3_500_000,AVVideoProfileLevelKey:AVVideoProfileLevelH264HighAutoLevel,AVVideoMaxKeyFrameIntervalKey:60]])
input.expectsMediaDataInRealTime = false
let adapter = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput:input,sourcePixelBufferAttributes:[kCVPixelBufferPixelFormatTypeKey as String:kCVPixelFormatType_32BGRA,kCVPixelBufferWidthKey as String:width,kCVPixelBufferHeightKey as String:height,kCVPixelBufferCGImageCompatibilityKey as String:true,kCVPixelBufferCGBitmapContextCompatibilityKey as String:true])
writer.add(input);writer.shouldOptimizeForNetworkUse = true
guard writer.startWriting() else { fatalError("MP4 encoder: \(String(describing:writer.error))") }
writer.startSession(atSourceTime:.zero)
for frame in 0..<count {
    while !input.isReadyForMoreMediaData && writer.status == .writing { Thread.sleep(forTimeInterval:0.002) }
    guard writer.status == .writing else { fatalError("Encoding: \(String(describing:writer.error))") }
    autoreleasepool {
        var optional: CVPixelBuffer?
        let status = CVPixelBufferPoolCreatePixelBuffer(nil,adapter.pixelBufferPool!,&optional)
        guard status == kCVReturnSuccess,let buffer = optional else { fatalError("Pixel buffer \(status)") }
        CVPixelBufferLockBaseAddress(buffer,[])
        let ctx = CGContext(data:CVPixelBufferGetBaseAddress(buffer),width:width,height:height,bitsPerComponent:8,bytesPerRow:CVPixelBufferGetBytesPerRow(buffer),space:CGColorSpaceCreateDeviceRGB(),bitmapInfo:CGImageAlphaInfo.premultipliedFirst.rawValue|CGBitmapInfo.byteOrder32Little.rawValue)!
        render(ctx,Double(frame)/Double(fps))
        if [45,150,285,390].contains(frame),let img = ctx.makeImage() {
            let url = output.deletingPathExtension().appendingPathExtension("frame-\(frame).png")
            if let dest = CGImageDestinationCreateWithURL(url as CFURL,"public.png" as CFString,1,nil) { CGImageDestinationAddImage(dest,img,nil);CGImageDestinationFinalize(dest) }
        }
        CVPixelBufferUnlockBaseAddress(buffer,[])
        if !adapter.append(buffer,withPresentationTime:CMTime(value:Int64(frame),timescale:Int32(fps))) { fatalError("Append: \(String(describing:writer.error))") }
    }
}
writer.endSession(atSourceTime:CMTime(value:15,timescale:1));input.markAsFinished()
let done = DispatchSemaphore(value:0);writer.finishWriting { done.signal() }
guard done.wait(timeout:.now()+45) == .success,writer.status == .completed else { fatalError("Finish: \(String(describing:writer.error))") }
print("Created \(output.path): 1280×720, 30 fps, H.264, 15 s, silent")
