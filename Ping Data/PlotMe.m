data=dlmread('small ping 2.csv',',');

%x=data(131072*4+(1:131072),1);
%dlmwrite('water ping 4.csv',x);
plot(data)
