namespace AiLight.Core;

public static class RobotIcon {
    // Straight-alpha BGRA, 32x32. Both shells consume the same antialiased pixels.
    public static byte[] Pixels(OutputMode mode) {
        var pixels=new byte[32*32*4];
        (byte R,byte G,byte B) eye=mode switch{OutputMode.Red=>(255,99,71),OutputMode.Green=>(181,230,157),OutputMode.Yellow=>(255,215,0),OutputMode.Blue=>(100,149,237),_=>(128,128,128)};
        bool Round(double x,double y,double left,double top,double width,double height,double radius){if(x<left||y<top||x>left+width||y>top+height)return false;double dx=Math.Max(Math.Abs(x-left-width/2)-(width/2-radius),0),dy=Math.Max(Math.Abs(y-top-height/2)-(height/2-radius),0);return dx*dx+dy*dy<=radius*radius;}
        for(int y=0;y<32;y++)for(int x=0;x<32;x++) {
            int red=0,green=0,blue=0,count=0;
            for(int sy=0;sy<4;sy++)for(int sx=0;sx<4;sx++) {
                double px=x+(sx+0.5)/4,py=y+(sy+0.5)/4;
                if(!Round(px,py,1,1,30,30,8.5))continue;
                (int R,int G,int B) color=(40,47,37);
                if(Round(px,py,15,4.5,2,5.5,1))color=(245,243,236);
                if((px-16)*(px-16)+(py-5)*(py-5)<=2.25)color=(eye.R,eye.G,eye.B);
                if(Round(px,py,4,9,24,18,7.5))color=(245,243,236);
                if(Round(px,py,6.5,11.5,19,13,5))color=(38,51,40);
                if(Round(px,py,10.5,15.5,4,5.5,2)||Round(px,py,17.5,15.5,4,5.5,2))color=(eye.R,eye.G,eye.B);
                red+=color.R;green+=color.G;blue+=color.B;count++;
            }
            if(count==0)continue;int offset=(y*32+x)*4;pixels[offset]=(byte)(blue/count);pixels[offset+1]=(byte)(green/count);pixels[offset+2]=(byte)(red/count);pixels[offset+3]=(byte)(255*count/16);
        }
        return pixels;
    }
}
