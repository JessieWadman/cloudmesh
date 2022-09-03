FROM --platform=linux/arm64 public.ecr.aws/amazonlinux/amazonlinux:2
RUN yum -y update -y && yum -y upgrade
WORKDIR /app
COPY /publish .
ENTRYPOINT ["/app/EcsActorsExample"]