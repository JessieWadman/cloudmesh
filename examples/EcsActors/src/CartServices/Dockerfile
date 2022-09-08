FROM mcr.microsoft.com/dotnet/aspnet:6.0
#RUN yum -y update -y && yum -y upgrade && yum install aspnetcore-runtime-6.0
RUN apt -y update && apt -y upgrade
WORKDIR /app
COPY /publish .
ENTRYPOINT ["/app/CartServices"]